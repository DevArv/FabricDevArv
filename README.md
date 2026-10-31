# DevArv: desarrollo automatizado con agentes de Claude

Tablero Kanban en **ASP.NET Core Razor Pages** (.NET 10 + PostgreSQL). Cada columna es una etapa del flujo.
Las tareas avanzan solas por las columnas de agente: Claude Code implementa en una rama de tu repositorio de GitHub,
otro agente revisa, el servidor abre el pull request y una persona aprueba.

```
┌───────────────────────── Servidor (DevArv.Web) ─────────────────────────┐
│ Razor Pages: tablero, tareas, proyectos, flujo, PCs                     │
│ Motor de flujo (WorkflowService) · Orquestador (crea PRs, vigila)       │
│ API /api/worker (clave por PC) · PostgreSQL · adjuntos en disco         │
└──────────────▲───────────────────────────────────────┬──────────────────┘
               │ HTTPS: reclamar trabajo, log, resultado│ API de GitHub (PRs)
┌──────────────┴──────────── PC / VM (DevArv.Worker) ───┴──────────────────┐
│ git clone + worktree por tarea → claude -p (stream-json) → commit + push │
└──────────────────────────────────────────────────────────────────────────┘
```

## Flujo por defecto

| Columna | Tipo | ✓ (bien / aprobar) | ⊘ (rechazar / pedir cambios) |
|---|---|---|---|
| 📥 Backlog | Manual | Aprobación Tarea | |
| ✏️ Requiere Mejoras | Manual | Aprobación Tarea | |
| ✅ Aprobación Tarea | Aprobación humana | Desarrollador | Requiere Mejoras |
| 💻 Desarrollador | Agente (opus, commit + push) | Revisor de Código | |
| 🔍 Revisor de Código | Agente (sonnet, veredicto) | Creador de PR | Desarrollador |
| 🔀 Creador de PR | Sistema (abre el PR) | Aprobación Producto | |
| 🧑‍💼 Aprobación Producto | Aprobación humana | Revisión Código Manual | Desarrollador |
| 🧐 Revisión Código Manual | Aprobación humana | Pendiente Deploy | Desarrollador |
| 🚀 Pendiente Deploy → 🧪 Pendiente Prueba → 🏁 Terminado | Manual | | |
| 🗑️ Descartado | Final | | |

Todo se edita en **Flujo**: nombres, orden, modelo, permisos, herramientas, prompts y destinos ✓/⊘.
Al crear una tarea puedes marcar «Enviar directo al agente desarrollador» para saltarte Backlog y la aprobación.

**Cómo decide el revisor:** su prompt le exige terminar con `VEREDICTO: APROBADO` o `VEREDICTO: RECHAZADO`.
Si rechaza, sus hallazgos se guardan como comentario y vuelven al desarrollador en su siguiente prompt.
**Pedir cambios** (humano) funciona igual: el texto se inyecta en el prompt del agente.

## Requisitos

- **Servidor:** .NET 10 SDK y PostgreSQL 14 o superior.
- **Cada PC worker:** .NET 10 Runtime (o SDK), `git` y [Claude Code](https://docs.claude.com/en/docs/claude-code)
  con sesión iniciada (`claude` y luego login) o una `ANTHROPIC_API_KEY`.
  En Windows conviene el instalador nativo (`claude.exe`). Si lo instalaste con npm, el worker detecta `claude.cmd` y ejecuta `node cli.js` directamente.
- **GitHub:** dos tokens fine-grained (o uno solo si prefieres):
  - Servidor: *Pull requests: Read & write*, *Contents: Read*, *Metadata: Read* sobre los repos que vas a conectar.
  - Workers: *Contents: Read & write* (clonar y hacer push de las ramas `devarv/*`).

## Puesta en marcha

### 1. Base de datos

```sql
CREATE USER devarv WITH PASSWORD 'una-clave-segura';
CREATE DATABASE devarv OWNER devarv;
```

### 2. Servidor

```bash
cd src/DevArv.Web
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=devarv;Username=devarv;Password=una-clave-segura"
dotnet user-secrets set "GitHub:Token" "github_pat_..."
dotnet user-secrets set "Admin:Email" "tu@correo.com"
dotnet user-secrets set "Admin:Password" "UnaClaveLarga123"

# Recomendado: generar la migración inicial (si no existe, el esquema se crea con EnsureCreated)
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial

dotnet run
```

Abre `https://localhost:5001` y entra con el administrador. Si no configuras `Admin:*`, la página de registro queda
abierta sólo hasta que exista el primer usuario. Para permitir más registros: `Auth:AllowRegistration=true`.

En producción usa variables de entorno (`ConnectionStrings__Default`, `GitHub__Token`, `App__PublicUrl`…) y
ponlo detrás de HTTPS (IIS, Nginx o Azure App Service).

### 3. Conectar repositorios

**Proyectos → Conectar repositorio**: pega `org/repo` o la URL de GitHub y pulsa «Probar conexión».
Opcional: «Instrucciones para los agentes», que se agregan al system prompt (stack, convenciones, qué no tocar).
Para reglas del código, lo ideal es tener un `CLAUDE.md` en el repositorio, que Claude Code lee solo.

### 4. Registrar una PC y arrancar el worker

1. **Mis PCs → Registrar PC** (ej. `agents-vm`) y copia la clave que aparece (se muestra una sola vez).
2. En esa PC, crea `src/DevArv.Worker/appsettings.Local.json`:

```json
{
  "Worker": {
    "Server": { "Url": "https://devarv.miempresa.com", "ApiKey": "dvw_..." },
    "Workspace": { "Root": "D:\\devarv-workspace" },
    "Git": { "GitHubToken": "github_pat_..." },
    "MaxParallelJobs": 1
  }
}
```

3. Ejecuta `dotnet run --project src/DevArv.Worker`. En el tablero la PC aparecerá «En línea».

**Como servicio de Windows:**

```powershell
dotnet publish src/DevArv.Worker -c Release -o C:\devarv-worker
sc.exe create "DevArv Worker" binPath= "C:\devarv-worker\DevArv.Worker.exe" start= auto obj= ".\TuUsuario" password= "..."
sc.exe start "DevArv Worker"
```

Ejecuta el servicio con **tu usuario** (no LocalSystem) para que use tu sesión de Claude Code y tus credenciales de git,
o define `Worker:Claude:AnthropicApiKey`. En Linux funciona igual como unidad de **systemd** (`Type=notify`).

### 5. Usarlo

**+ Nueva tarea** → título, proyecto, rama base, descripción en Markdown y hasta 15 adjuntos (imágenes, PDF o HTML; también puedes pegar capturas con Ctrl+V).
Arrastra la tarjeta a «Aprobación Tarea» → **Aprobar** → el worker la toma. Abre la tarjeta para ver el log en vivo,
el costo y los tokens de cada ejecución, el prompt enviado, los archivos modificados y el enlace al PR.

## Qué hace el worker en cada etapa de agente

1. `git clone` (una vez por repo) y `git fetch`; crea o reutiliza el worktree `work/task-{id}` en la rama `devarv/{id}-{slug}`
   (parte de la rama remota si ya existe, o de la rama base si no).
2. Descarga los adjuntos a `attachments/task-{id}` y los habilita con `--add-dir`.
3. Ejecuta `claude -p --output-format stream-json --verbose --model … --permission-mode … --allowedTools …` con el prompt por stdin.
   El log se envía al servidor cada 2 segundos y el botón **Cancelar** detiene el proceso.
4. Si la etapa hace push: `git add -A`, commit y `git push origin HEAD:refs/heads/<rama>`. Si no hubo cambios, la etapa falla.
5. Reporta resultado, costo, tokens, turnos y archivos modificados.

El token de GitHub del worker viaja en una cabecera HTTP de git (igual que `actions/checkout`) y nunca se guarda en `.git/config`.

## Seguridad

- **Adjuntos:** máximo 15 por tarea y 20 MB cada uno (configurable en `Attachments`). El tipo se valida por **contenido**
  (firma PNG/JPEG/GIF/WEBP, `%PDF-`, documento HTML en UTF-8 válido), no sólo por extensión, y la extensión debe coincidir.
  SVG, ejecutables y cualquier otro formato se rechazan. Se guardan con nombre aleatorio fuera de `wwwroot`.
  El HTML nunca se muestra en el dominio de la app: se descarga con `Content-Security-Policy: sandbox` y `nosniff`.
- **Markdown:** el HTML crudo se escapa y los enlaces sólo pueden ser http(s), mailto o relativos.
- **Workers:** cada PC tiene su propia clave (se guarda sólo el hash SHA-256), se puede deshabilitar o rotar,
  y una PC sólo puede leer y reportar sus propias ejecuciones.
- **Agentes:** el desarrollador usa `acceptEdits` con una lista blanca de comandos; el revisor no puede editar (`Edit`/`Write` prohibidos).
  Usa `bypassPermissions` sólo en VMs aisladas y desechables.

## Estructura

```
src/DevArv.Contracts   DTOs compartidos servidor ↔ worker
src/DevArv.Web         Razor Pages, EF Core (Npgsql), Identity, API de workers, orquestador
src/DevArv.Worker      Servicio (Windows/systemd) que ejecuta Claude Code y git
tests/DevArv.Tests     xUnit: validación de archivos, veredictos, prompts, parser de stream-json, argumentos del CLI
```

## Próximos pasos sugeridos

- API pública con token para crear tareas desde otros sistemas (Jira, formularios).
- Webhooks de GitHub para mover tareas al hacer merge del PR.
- Dependencias entre tareas, métricas de costo por proyecto y modo TV.
- Agente «Analista» antes de Aprobación Tarea que mejore la descripción y estime complejidad.
