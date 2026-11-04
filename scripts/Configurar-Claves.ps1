<#
.SYNOPSIS
    Guarda la API key de Anthropic y el token de GitHub en los appsettings.Local.json del servidor y del worker.

.DESCRIPTION
    Escribe (o actualiza sin borrar lo demás) estos archivos, que git ignora:
      src/DevArv.Web/appsettings.Local.json     → GitHub:Token (crear y consultar pull requests)
      src/DevArv.Worker/appsettings.Local.json  → Worker:Claude:AnthropicApiKey, Worker:Git:GitHubToken,
                                                  Worker:Server:Url, Worker:Server:ApiKey, Worker:Workspace:Root
    Las claves se piden ocultas. Después verifica que la API key responda y que el token alcance los repositorios.

    El appsettings es la única fuente de credenciales: el worker no usa la sesión de `claude login`,
    el Credential Manager de git ni variables de entorno de la PC, y no toma trabajos si falta alguna.

.EXAMPLE
    ./scripts/Configurar-Claves.ps1
    ./scripts/Configurar-Claves.ps1 -Destino Worker -Repositorio DevArv/ShopyCube
#>
[CmdletBinding()]
param(
    [ValidateSet('Ambos', 'Servidor', 'Worker')]
    [string] $Destino = 'Ambos',

    # Repositorios (owner/repo) a los que el token de GitHub debe tener acceso.
    [string[]] $Repositorio = @('DevArv/ShopyCube'),

    # Pide un token distinto para el worker (si no, se usa el mismo del servidor).
    [switch] $TokenSeparadoParaWorker,

    # No llama a las APIs de Anthropic ni de GitHub para validar.
    [switch] $SinVerificar,

    # Valores no interactivos (útiles en automatización; evita escribir claves en el historial de la consola).
    [string] $AnthropicApiKey,
    [string] $GitHubToken,
    [string] $WorkerGitHubToken,
    [string] $ServerUrl,
    [string] $WorkerApiKey,
    [string] $WorkspaceRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$webFile = Join-Path $root 'src/DevArv.Web/appsettings.Local.json'
$workerFile = Join-Path $root 'src/DevArv.Worker/appsettings.Local.json'

# ------------------------------------------------------------------ utilidades

function Read-Secret([string] $Prompt) {
    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)).Trim() }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Read-Plain([string] $Prompt, [string] $Default) {
    $suffix = if ($Default) { " [$Default]" } else { '' }
    $value = (Read-Host -Prompt "$Prompt$suffix").Trim()
    if ($value) { return $value } else { return $Default }
}

function ConvertTo-OrderedTable($Value) {
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $table = [ordered]@{}
        foreach ($p in $Value.PSObject.Properties) { $table[$p.Name] = ConvertTo-OrderedTable $p.Value }
        return $table
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return , @($Value | ForEach-Object { ConvertTo-OrderedTable $_ })
    }
    return $Value
}

function Read-JsonFile([string] $Path) {
    if (Test-Path $Path) {
        $text = Get-Content -Raw -Path $Path
        if ($text -and $text.Trim()) { return ConvertTo-OrderedTable ($text | ConvertFrom-Json) }
    }
    return [ordered]@{}
}

function Get-Value($Table, [string] $Path) {
    $node = $Table
    foreach ($part in $Path.Split(':')) {
        if ($node -isnot [System.Collections.IDictionary] -or -not $node.Contains($part)) { return $null }
        $node = $node[$part]
    }
    return $node
}

function Set-Value($Table, [string] $Path, $Value) {
    $parts = $Path.Split(':')
    $node = $Table
    for ($i = 0; $i -lt $parts.Length - 1; $i++) {
        if (-not $node.Contains($parts[$i]) -or $node[$parts[$i]] -isnot [System.Collections.IDictionary]) {
            $node[$parts[$i]] = [ordered]@{}
        }
        $node = $node[$parts[$i]]
    }
    $node[$parts[-1]] = $Value
}

function Remove-Value($Table, [string] $Path) {
    $parts = $Path.Split(':')
    $parent = Get-Value $Table ($parts[0..($parts.Length - 2)] -join ':')
    if ($parent -is [System.Collections.IDictionary] -and $parent.Contains($parts[-1])) { $parent.Remove($parts[-1]) }
}

function Write-JsonFile([string] $Path, $Table) {
    $json = $Table | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "  ✔ $Path" -ForegroundColor Green
}

function Test-AnthropicKey([string] $Key) {
    try {
        $headers = @{ 'x-api-key' = $Key; 'anthropic-version' = '2023-06-01' }
        Invoke-RestMethod -Uri 'https://api.anthropic.com/v1/models?limit=1' -Headers $headers -Method Get | Out-Null
        Write-Host '  ✔ La API key de Anthropic es válida.' -ForegroundColor Green
    }
    catch {
        Write-Warning "La API de Anthropic rechazó la clave: $($_.Exception.Message)"
    }
}

function Test-GitHubToken([string] $Token, [string] $Label) {
    $headers = @{
        Authorization          = "Bearer $Token"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'           = 'DevArv-Setup'
    }
    foreach ($repo in $Repositorio) {
        try {
            $r = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo" -Headers $headers -Method Get
            Write-Host "  ✔ Token $Label tiene acceso de lectura a $($r.full_name) (rama por defecto: $($r.default_branch))." -ForegroundColor Green
        }
        catch {
            Write-Warning "El token $Label no alcanza $repo : $($_.Exception.Message). Revisa que el token incluya ese repositorio y sus permisos."
        }
    }
}

function Assert-Prefix([string] $Value, [string[]] $Prefixes, [string] $Name) {
    if ($Value -and -not ($Prefixes | Where-Object { $Value.StartsWith($_) })) {
        Write-Warning "$Name no empieza por $($Prefixes -join ' / '). Verifica que copiaste el valor correcto."
    }
}

# ------------------------------------------------------------------ captura de valores

$configureWeb = $Destino -in 'Ambos', 'Servidor'
$configureWorker = $Destino -in 'Ambos', 'Worker'

Write-Host ''
Write-Host 'DevArv · configuración de claves' -ForegroundColor Cyan
Write-Host 'Los valores se guardan en appsettings.Local.json (ignorado por git). Nunca los subas al repositorio.'
Write-Host ''

if (-not $GitHubToken) {
    $GitHubToken = Read-Secret 'Token de GitHub (github_pat_… con acceso a los repos de los proyectos)'
}
if (-not $GitHubToken) { throw 'El token de GitHub es obligatorio.' }
Assert-Prefix $GitHubToken @('github_pat_', 'ghp_') 'El token de GitHub'

if ($configureWorker) {
    if (-not $WorkerGitHubToken) {
        $WorkerGitHubToken = if ($TokenSeparadoParaWorker) {
            Read-Secret 'Token de GitHub del worker (Contents: Read and write)'
        } else { $GitHubToken }
    }
    Assert-Prefix $WorkerGitHubToken @('github_pat_', 'ghp_') 'El token de GitHub del worker'

    if (-not $AnthropicApiKey) {
        $AnthropicApiKey = Read-Secret 'API key de Anthropic (sk-ant-api03-…)'
    }
    if (-not $AnthropicApiKey) { throw 'La API key de Anthropic es obligatoria.' }
    Assert-Prefix $AnthropicApiKey @('sk-ant-api') 'La API key de Anthropic'

    $current = Read-JsonFile $workerFile
    $defaultUrl = (Get-Value $current 'Worker:Server:Url')
    if (-not $defaultUrl) { $defaultUrl = 'https://localhost:5001' }
    if (-not $ServerUrl) { $ServerUrl = Read-Plain 'URL del servidor DevArv' $defaultUrl }
    if (-not $PSBoundParameters.ContainsKey('WorkerApiKey')) {
        $WorkerApiKey = Read-Secret 'Clave de la PC (dvw_… de «Mis PCs»; Enter si aún no la tienes: el worker no arrancará sin ella)'
    }
    if (-not $WorkspaceRoot) {
        $WorkspaceRoot = Read-Plain 'Carpeta de trabajo del worker (Enter = ~/devarv-workspace)' (Get-Value $current 'Worker:Workspace:Root')
    }
}

# ------------------------------------------------------------------ escritura

Write-Host ''
Write-Host 'Guardando:' -ForegroundColor Cyan

if ($configureWeb) {
    $web = Read-JsonFile $webFile
    Set-Value $web 'GitHub:Token' $GitHubToken
    Write-JsonFile $webFile $web
}

if ($configureWorker) {
    $worker = Read-JsonFile $workerFile
    Set-Value $worker 'Worker:Git:GitHubToken' $WorkerGitHubToken
    Set-Value $worker 'Worker:Server:Url' $ServerUrl
    if ($WorkerApiKey) { Set-Value $worker 'Worker:Server:ApiKey' $WorkerApiKey }
    if ($WorkspaceRoot) { Set-Value $worker 'Worker:Workspace:Root' $WorkspaceRoot }

    Set-Value $worker 'Worker:Claude:AnthropicApiKey' $AnthropicApiKey
    # Las credenciales de Anthropic sólo van en AnthropicApiKey; el worker descarta las de Claude:Environment.
    foreach ($name in 'ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'CLAUDE_CODE_OAUTH_TOKEN') {
        Remove-Value $worker "Worker:Claude:Environment:$name"
    }
    Write-JsonFile $workerFile $worker
}

# ------------------------------------------------------------------ verificación

if (-not $SinVerificar) {
    Write-Host ''
    Write-Host 'Verificando:' -ForegroundColor Cyan
    Test-GitHubToken $GitHubToken 'del servidor'
    if ($configureWorker -and $WorkerGitHubToken -ne $GitHubToken) { Test-GitHubToken $WorkerGitHubToken 'del worker' }
    if ($configureWorker) { Test-AnthropicKey $AnthropicApiKey }
}

Write-Host ''
Write-Host 'Listo. Reinicia el servidor y el worker para que tomen los cambios.' -ForegroundColor Cyan
