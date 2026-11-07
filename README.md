# DevArv: desarrollo automatizado con agentes de Claude

Tablero Kanban en **ASP.NET Core Razor Pages** (.NET 10 + PostgreSQL). Cada columna es una etapa del flujo.
Las tareas avanzan solas por las columnas de agente: Claude Code implementa en una rama de tu repositorio de GitHub,
otro agente revisa, el servidor abre el pull request y una persona aprueba.

```
┌───────────────────────── Servidor (DevArv.Web) ─────────────────────────┐
│ Razor Pages: tablero, tareas, proyectos, flujo, PCs                     │
│ Motor de flujo (WorkflowService) · Orquestador (crea PRs, vigila)       │
│ API /api/worker (clave por PC) · PostgreSQL (incluye los adjuntos)      │
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

**Merge del pull request:** cada minuto el servidor consulta en GitHub los pull requests abiertos de las tareas. Cuando
uno se integra (merge, hecho a mano en GitHub o por la acción «Hacer merge del pull request»), la tarea entra sola a la
columna marcada en **Flujo** con «Al hacer merge del pull request en GitHub, la tarea entra aquí» (por defecto
🚀 Pendiente Deploy) y queda registrado en su historial. Sólo se mueven las tareas que están en una columna anterior y
no finalizada: no se regresa una tarea que ya avanzó ni se reabre una descartada o archivada. Si se crea un pull request
nuevo para la tarea, se vuelve a vigilar.

**Conflictos del pull request:** en esa misma consulta el servidor lee si GitHub puede integrar el pull request
(`mergeable` / `mergeable_state`). Si informa conflictos con la rama base, la tarjeta muestra el estado
«⚠️ El PR tiene conflictos» (normalmente en 🧑‍💼 Aprobación Producto) y un botón 🔧 (también «🔧 Arreglar conflictos» en
el detalle de la tarea). Al pulsarlo, la tarea regresa a 💻 Desarrollador: el worker integra `origin/<rama base>` en la
rama de trabajo (`git merge`), deja los archivos en conflicto para el agente, que los resuelve (instrucciones en la
variable `{{conflicts}}` del prompt, o al final si la plantilla no la usa), y luego hace el commit del merge y el push.
Si quedan marcadores de conflicto no hace push y la ejecución falla («Reintentar» vuelve a integrar la rama base).
Después sigue el flujo normal (Revisor de Código → Creador de PR, que reutiliza el PR abierto → Aprobación Producto).
El estado desaparece solo cuando GitHub deja de informar conflictos; el botón no se muestra mientras la tarea tiene una
ejecución en cola o en curso. Los workers deben actualizarse junto con el servidor para soportar este arreglo.

**Pestañas informativas de la tarea:** cuando la tarea llega a 🔀 Creador de PR (está en esa columna, ya tuvo una
ejecución en ella o ya tiene pull request), el detalle de la ejecución se muestra en pestañas:
- **Log**: la ejecución seleccionada con su cantidad de líneas, «Resumen» (resultado y archivos modificados),
  «Log completo» y «Copiar log».
- **Pruebas unitarias** y **Pruebas de QA**: las secciones `## Pruebas unitarias` y `## Pruebas QA` del resumen final
  de la última ejecución exitosa del desarrollador que las incluya. El servidor le pide al desarrollador esas secciones
  en el prompt (variable `{{report}}`, o al final si la plantilla no la usa; no aplica al arreglo de conflictos).
- **Capturas**: imágenes (PNG, JPG, GIF o WEBP) con los cambios aplicados, que se suben desde la misma pestaña.
  Cuentan para el máximo de archivos por tarea y no se envían al agente.

El punto azul junto a una pestaña indica que tiene contenido; la pestaña elegida se recuerda en la sesión del navegador.

**Skill de la etapa:** cada columna de agente puede indicar un *skill* de Claude Code (campo **Skill** en **Flujo**).
El Revisor de Código usa por defecto `revision`, el mismo skill de ShopyCube (`.claude/skills/revision/SKILL.md` en el
repositorio del proyecto). El servidor agrega `Skill(<nombre>)` a las herramientas permitidas y pone en el prompt la
instrucción de invocarlo (variable `{{skill}}`, o al final si la plantilla no la usa). Si el repositorio no tiene ese
skill, el agente lo dice y revisa con las pautas del prompt; el formato de `VEREDICTO` siempre manda.

## Guía de configuración paso a paso

DevArv tiene dos aplicaciones que se configuran por separado:

- **Servidor (`src/DevArv.Web`)**: el tablero web, la base de datos y la conexión con GitHub para abrir los PR.
- **Worker (`src/DevArv.Worker`)**: corre en una PC o VM, toma las tareas del servidor y ejecuta Claude Code y git.
  Puede estar en la misma PC que el servidor o en otra.

Sigue los pasos en orden: algunos datos sólo existen cuando el paso anterior está hecho (por ejemplo, la clave de la PC
se genera en el servidor ya en marcha).

### Resumen: qué hay que configurar

| # | Qué | Dónde se guarda | Para qué |
|---|---|---|---|
| 1 | Programas: .NET, PostgreSQL, git, Claude Code | En cada PC | Compilar y ejecutar las apps y los agentes |
| 2 | Token de GitHub | `GitHub:Token` (servidor) y `Worker:Git:GitHubToken` (worker) | Leer los repos, hacer push de ramas y abrir PRs |
| 3 | Credencial de Claude: suscripción Pro/Max o API key | `Worker:Claude:OAuthToken` o `Worker:Claude:AnthropicApiKey` (worker) | Que Claude Code pueda trabajar |
| 4 | Base de datos PostgreSQL | `EnvironmentData` (servidor) | Guardar tareas, flujo, usuarios y PCs |
| 5 | Administrador y URL pública | `Admin`, `App:PublicUrl` (servidor) | Entrar al tablero y enlazar los PR a la tarea |
| 6 | Arrancar el servidor | — | Crear el esquema y el flujo por defecto |
| 7 | Conectar el repositorio (ej. ShopyCube) | Pantalla **Proyectos** | Indicar a qué repo y rama van las tareas |
| 8 | Registrar la PC worker | Pantalla **Mis PCs** → `EnvironmentData` (worker) | Que el worker pueda pedir trabajo al servidor |
| 9 | Configurar y arrancar el worker | `src/DevArv.Worker/appsettings.Local.json` | Ejecutar las tareas |
| 10 | Probar con una tarea | Tablero | Confirmar que todo el circuito funciona |

### Cómo se lee la configuración

Toda la configuración se lee **sólo de los archivos appsettings** de cada app, a través de
`EnvironmentConfig/EnvironmentTools` (el mismo patrón de ShopyCube), en este orden; cada archivo sobrescribe al anterior:

1. `appsettings.json`: valores por defecto, versionado en git. **No pongas secretos aquí.**
2. `appsettings.{Entorno}.json`: opcional, por entorno (`Development`, `Production`).
3. `appsettings.Local.json`: **tus datos y credenciales**. Está en `.gitignore`, así que nunca se sube al repositorio.
   Hay una plantilla `appsettings.Local.example.json` junto a cada app.

No se usan user-secrets, variables de entorno ni argumentos de línea de comandos para la configuración de la app
(sólo las variables propias del host, como `ASPNETCORE_ENVIRONMENT` o las URLs de `launchSettings.json`). Si falta un
dato obligatorio, la app **no arranca** y el mensaje dice exactamente qué clave falta.

**El script `scripts/Configurar-Claves.ps1`** hace los pasos 2 a 5 y 9 por ti: pregunta cada dato (las claves sin
mostrarlas), escribe los `appsettings.Local.json` sin borrar lo que ya tengan y comprueba que el token de GitHub
alcance los repos y que la API key de Anthropic responda (el token de suscripción no se puede verificar fuera de Claude Code). Está probado con PowerShell 7 (`pwsh`, disponible en Windows, Linux y macOS: `brew install powershell`); en Windows
PowerShell 5.1 no se ha probado. Si Windows bloquea scripts, ejecútalo así:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Configurar-Claves.ps1
```

---

### Paso 1. Instalar los programas

| Dónde | Qué instalar | Comprobación |
|---|---|---|
| Servidor | [.NET 10 SDK](https://dotnet.microsoft.com/download) | `dotnet --version` → `10.x` |
| Servidor (o un servidor de BD) | PostgreSQL 14 o superior | `psql --version` |
| Cada PC worker | .NET 10 (SDK o Runtime) | `dotnet --version` |
| Cada PC worker | `git` | `git --version` |
| Cada PC worker | [Claude Code](https://docs.claude.com/en/docs/claude-code) | `claude --version` |
| Cada PC worker | Lo que necesite compilar el repo de tus proyectos (para ShopyCube: **.NET 8 SDK**) | `dotnet --list-sdks` |

En Windows conviene el instalador nativo de Claude Code (`claude.exe`). Si lo instalaste con npm, el worker detecta
`claude.cmd` y ejecuta `node cli.js` directamente. **No hace falta `claude login`**: el worker usa la credencial del paso 3.

### Paso 2. Crear el token de GitHub

El servidor lo usa para probar la conexión con el repo, listar ramas y abrir los pull requests; el worker, para clonar
y hacer push de las ramas de trabajo.

1. En GitHub: foto de perfil → **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**.
2. **Resource owner**: el dueño de los repos (p. ej. `DevArv`).
3. **Repository access → Only select repositories**: los repos donde se harán desarrollos (p. ej. `ShopyCube`).
4. **Permissions → Repository permissions**:

   | Permiso | Servidor | Worker | Un solo token para ambos |
   |---|---|---|---|
   | Contents | Read (Read & write si usas la acción «Hacer merge del pull request») | **Read & write** | **Read & write** |
   | Pull requests | **Read & write** | — | **Read & write** |
   | Metadata | Read (obligatorio) | Read (obligatorio) | Read |

5. Copia el token (`github_pat_…`); GitHub sólo lo muestra una vez. Pon una fecha de expiración y anótala: cuando
   venza, repite el script con el token nuevo.

Puedes usar el mismo token en el servidor y en el worker (lo más simple) o uno distinto en cada uno
(`-TokenSeparadoParaWorker` en el script).

### Paso 3. Credencial de Claude (suscripción o API key)

Claude Code necesita una credencial para trabajar en las tareas. Hay dos opciones; **elige una sola**:

| | Suscripción Claude Pro/Max | API key de Anthropic |
|---|---|---|
| Costo | La cuota que ya pagas; el uso cuenta contra los límites de tu plan | Se paga por uso con saldo cargado en la consola, aparte de la suscripción |
| Clave | Token `sk-ant-oat…` | `sk-ant-api…` |
| Se guarda en | `Worker:Claude:OAuthToken` | `Worker:Claude:AnthropicApiKey` |
| Límites | Los de tu plan por ventanas de tiempo: con varias tareas seguidas pueden agotarse | Los de tu cuenta de la consola; puedes fijar un tope de gasto mensual |

**Opción A: suscripción.** En una PC con Claude Code instalado y con la sesión iniciada con tu cuenta:

1. Ejecuta `claude setup-token`.
2. Copia el token `sk-ant-oat…` que muestra (sólo aparece una vez).

Revisa en la documentación de Anthropic que los términos de tu plan permitan este uso automatizado.

**Opción B: API key.** Entra a [console.anthropic.com](https://console.anthropic.com) → **Plans & Billing** (carga saldo)
→ **API Keys → Create Key** y copia la clave (`sk-ant-api…`). El tablero muestra el costo de cada ejecución.

Sólo se guarda en el worker. Es la **única** credencial de Claude que se usa: el worker no usa la sesión de
`claude login`, descarta cualquier credencial de Anthropic que tenga la PC o `Worker:Claude:Environment`, y **no arranca
si defines las dos** (con las dos, Claude Code usaría la API key y se cobraría por uso).

### Paso 4. Crear la base de datos

En PostgreSQL, con un usuario administrador (`psql -U postgres`):

```sql
CREATE USER devarv WITH PASSWORD 'una-clave-segura';
CREATE DATABASE devarv OWNER devarv;
```

Estos datos van en la sección `EnvironmentData` de `src/DevArv.Web/appsettings.Local.json`:

| Clave | Ejemplo | Descripción |
|---|---|---|
| `Host` | `localhost` | Servidor de PostgreSQL |
| `Port` | `5432` | Puerto (1–65535) |
| `Database` | `devarv` | Nombre de la base de datos |
| `Username` | `devarv` | Usuario dueño de la base |
| `Password` | `una-clave-segura` | Contraseña del usuario |

Las tablas no se crean a mano: el servidor las crea al arrancar (paso 6).

### Paso 5. Configurar el servidor

En la carpeta del repo, ejecuta el script para el servidor:

```powershell
./scripts/Configurar-Claves.ps1 -Destino Servidor -Repositorio DevArv/ShopyCube
```

Te pedirá los datos de la base (paso 4) y el token de GitHub (paso 2), y comprobará que el token alcanza
`DevArv/ShopyCube`. Después, abre `src/DevArv.Web/appsettings.Local.json` y completa el administrador y la URL
(o cópialo de `appsettings.Local.example.json` y llena todo a mano):

```json
{
  "EnvironmentData": {
    "Host": "localhost", "Port": "5432", "Database": "devarv",
    "Username": "devarv", "Password": "una-clave-segura"
  },
  "App": { "PublicUrl": "https://localhost:5001" },
  "Admin": { "Email": "tu@correo.com", "Password": "UnaClaveLarga123" },
  "GitHub": { "Token": "github_pat_..." }
}
```

| Clave | Obligatoria | Descripción |
|---|---|---|
| `EnvironmentData:*` | Sí | Conexión a PostgreSQL (paso 4). Sin ella el servidor no arranca. |
| `GitHub:Token` | Sí, para conectar repos y abrir PRs | Token del paso 2. Sin él el servidor arranca, pero avisa en el log y «Probar conexión» falla. |
| `Admin:Email` / `Admin:Password` | Recomendada | Crea el primer usuario al arrancar (sólo si todavía no hay usuarios). Contraseña de 8+ caracteres con mayúscula, minúscula y número. Si la dejas vacía, la página de registro queda abierta hasta que se cree el primer usuario. |
| `App:PublicUrl` | Recomendada | URL con la que se abre el tablero. Se usa para el enlace «Ver tarea en DevArv» dentro de cada PR. |
| `App:TimeZone` | No | Zona horaria de las fechas (por defecto `America/Santo_Domingo`). |
| `Auth:AllowRegistration` | No | `true` permite que otras personas se registren solas. Por defecto `false`. |
| `Attachments:*` | No | Máximo de archivos por tarea (15) y tamaño (20 MB). El contenido se guarda en la base de datos (`Tasks_Attachments.Content`); cada subida se carga completa en memoria (hasta 300 MB con los valores por defecto). `StoragePath` (`App_Data/attachments`) sólo se usa para migrar los adjuntos antiguos. |

### Paso 6. Arrancar el servidor

```bash
dotnet dev-certs https --trust        # una sola vez por PC: certificado HTTPS de desarrollo
cd src/DevArv.Web

# Recomendado antes del primer arranque: generar la migración inicial.
# Si no existe ninguna migración, el esquema se crea con EnsureCreated y luego no se podrán aplicar migraciones.
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial

dotnet run
```

Al arrancar, el servidor crea las tablas, el flujo por defecto y el administrador. Abre `https://localhost:5001` y entra
con `Admin:Email` / `Admin:Password`.

**Comprobación:** ves el tablero con las columnas Backlog, Aprobación Tarea, Desarrollador, etc., y en el log no aparece
la advertencia `GitHub:Token no está configurado`.

### Paso 7. Conectar el repositorio

En el tablero: **📁 Proyectos → + Conectar repositorio**.

1. **Repositorio de GitHub**: `owner/repo` o la URL. Pulsa **Probar conexión**: debe mostrar
   «✓ Conexión correcta con …» y la lista de ramas, y completa la rama por defecto si está vacía o no existe.
   Si falla, el token del servidor no tiene acceso a ese repo (revisa el paso 2).
2. **Rama por defecto**: la rama de la que parten las tareas y contra la que se abre el PR.
3. **Prefijo de ramas de trabajo**: las ramas se llaman `{prefijo}{id}-{título}`.
4. **Instrucciones para los agentes** (opcional): se agregan al prompt de los agentes. Para las reglas del código es
   mejor un `CLAUDE.md` en el repositorio, que Claude Code lee solo.

**Ejemplo: ShopyCube**

| Campo | Valor |
|---|---|
| Repositorio | `DevArv/ShopyCube` |
| Rama por defecto | `master` (no `main`) |
| Prefijo de ramas | `sc-devarv-` → ramas `sc-devarv-{id}-{slug}`, en la línea de `sc-jvalerio-…` |

ShopyCube trae su propio `CLAUDE.md` con la arquitectura, los recursos `.resx`, los scripts SQL y las pruebas que deben
seguir los agentes.

### Paso 8. Registrar la PC worker

En el tablero: **🖥 Mis PCs**, escribe un nombre (ej. `agents-vm`) y pulsa **+ Registrar PC**. Copia la clave que
aparece (`dvw_…`): **se muestra una sola vez**. Si la pierdes, usa **Nueva clave** (la anterior deja de funcionar).

### Paso 9. Configurar y arrancar el worker

En la PC worker (con el repo clonado), ejecuta el script para el worker:

```powershell
./scripts/Configurar-Claves.ps1 -Destino Worker -Repositorio DevArv/ShopyCube
```

Te pedirá el token de GitHub (paso 2), la credencial de Claude (paso 3; reconoce por el prefijo si es suscripción o API key), la URL del servidor, la clave de la PC (paso 8)
y la carpeta de trabajo. El resultado es `src/DevArv.Worker/appsettings.Local.json`:

```json
{
  "EnvironmentData": { "Url": "https://localhost:5001", "ApiKey": "dvw_..." },
  "Worker": {
    "Workspace": { "Root": "D:\\devarv-workspace" },
    "Claude": { "OAuthToken": "sk-ant-oat01-..." },
    "Git": { "GitHubToken": "github_pat_..." },
    "MaxParallelJobs": 1
  }
}
```

| Clave | Obligatoria | Descripción |
|---|---|---|
| `EnvironmentData:Url` | Sí | URL del servidor (la de `App:PublicUrl`). Si el worker está en otra PC, debe poder abrirla y su certificado HTTPS debe ser de confianza. |
| `EnvironmentData:ApiKey` | Sí | Clave `dvw_…` del paso 8. |
| `Worker:Claude:OAuthToken` **o** `Worker:Claude:AnthropicApiKey` | Sí, una de las dos | Token de suscripción o API key del paso 3 (no ambas). |
| `Worker:Git:GitHubToken` | Sí | Token del paso 2 (Contents: Read & write). |
| `Worker:Workspace:Root` | No | Carpeta donde se clonan los repos. Por defecto `~/devarv-workspace`. Necesita espacio para un clon por repo y una copia por tarea. |
| `Worker:MaxParallelJobs` | No | Tareas al mismo tiempo en esta PC (por defecto 1). |
| `Worker:Claude:Executable` / `Worker:Git:Executable` | No | Ruta de `claude` y `git` si no están en el PATH. |
| `Worker:Git:AuthorName` / `AuthorEmail` | No | Autor de los commits de los agentes. |

Arráncalo:

```bash
dotnet run --project src/DevArv.Worker
```

**Comprobación:** el log muestra `DevArv.Worker en <PC> · servidor <url>` y las versiones de Claude Code y git, y en
**Mis PCs** la PC aparece **En línea** antes de un minuto.

#### Como servicio de Windows

```powershell
dotnet publish src/DevArv.Worker -c Release -o C:\devarv-worker
sc.exe create "DevArv Worker" binPath= "C:\devarv-worker\DevArv.Worker.exe" start= auto obj= ".\TuUsuario" password= "..."
sc.exe start "DevArv Worker"
```

Configura `src/DevArv.Worker/appsettings.Local.json` **antes** de publicar: el publish lo copia a `C:\devarv-worker`,
de donde el servicio lo lee. Si cambias una clave después, edita `C:\devarv-worker\appsettings.Local.json` y reinicia el
servicio. En Linux funciona igual como unidad de **systemd** (`Type=notify`).

### Paso 10. Probar con una tarea

1. **+ Nueva tarea**: título, proyecto, rama base, descripción en Markdown y hasta 15 adjuntos (imágenes, PDF o HTML;
   también puedes pegar capturas con Ctrl+V). Para una primera prueba, pide algo pequeño.
2. Arrastra la tarjeta a **Aprobación Tarea**, ábrela y pulsa **✓ Aprobar** (o marca «Enviar directo al agente
   desarrollador» al crearla).
3. El worker la toma: en la tarjeta verás el log en vivo, el prompt, el costo, los tokens y los archivos modificados.
4. Si el revisor aprueba, el servidor abre el pull request y la tarjeta muestra el enlace.

Si llegas al PR en GitHub, todo quedó bien configurado.

### En producción

- **Servidor:** `dotnet publish src/DevArv.Web -c Release`, deja `appsettings.Local.json` junto al ejecutable (con
  `EnvironmentData`, `GitHub:Token`, `Admin` y `App:PublicUrl` con la URL real) y ponlo detrás de HTTPS
  (IIS, Nginx o Azure App Service).
- **Workers:** `EnvironmentData:Url` debe apuntar a esa URL pública con un certificado válido.
- Guarda los `appsettings.Local.json` con permisos sólo para el usuario que ejecuta cada app.

### Solución de problemas

| Mensaje o síntoma | Causa | Solución |
|---|---|---|
| Servidor: `EnvironmentData:Host no puede estar vacío.` (o `Port`, `Database`…) | Falta la conexión a la base | Paso 4/5: completa `EnvironmentData` en `src/DevArv.Web/appsettings.Local.json` |
| `La sección de configuración 'EnvironmentData' no existe en appsettings.` | Falta la sección completa | Copia la plantilla `appsettings.Local.example.json` |
| Servidor: `Failed to connect to …` / `password authentication failed` | PostgreSQL no responde o datos incorrectos | Revisa que PostgreSQL esté corriendo y los datos del paso 4 |
| Log: `GitHub:Token no está configurado` | Falta el token del servidor | Paso 5 |
| «Probar conexión»: `GitHub 401` | Token inválido o vencido | Genera otro (paso 2) y repite el script |
| «Probar conexión»: `GitHub 404` | El token no incluye ese repo | Agrega el repo en *Repository access* del token |
| Al abrir el PR: `GitHub 403` | Falta *Pull requests: Read & write* en el token del servidor | Paso 2 |
| Worker: `EnvironmentData:ApiKey no puede estar vacío (clave generada en «Mis PCs»).` | Falta la clave de la PC | Pasos 8 y 9 |
| Worker: `EnvironmentData:Url debe ser la URL http(s) del servidor DevArv.` | URL vacía o mal escrita | Usa la URL completa, con `https://` |
| Worker: `Faltan en el appsettings del worker: Worker:Claude:OAuthToken … o Worker:Claude:AnthropicApiKey` | Falta la credencial de Claude o el token de GitHub | Pasos 2, 3 y 9 |
| Worker: `Define sólo una credencial de Claude… no ambas` | Hay `OAuthToken` y `AnthropicApiKey` | Deja una; el script limpia la otra |
| Worker: `No se pudo contactar al servidor` | URL incorrecta, servidor apagado o certificado no confiable | Abre la URL desde esa PC en el navegador; en local ejecuta `dotnet dev-certs https --trust` |
| Worker: `No se encontró Claude Code` | `claude` no está en el PATH | Instálalo (paso 1) o define `Worker:Claude:Executable` |
| La PC aparece «Sin conexión» | El worker no está corriendo o no llega al servidor | Revisa el log del worker |
| Worker: `Servidor respondió 401 (revisa EnvironmentData:ApiKey…)` | Clave incorrecta, PC deshabilitada o clave regenerada | En **Mis PCs**, habilita la PC o pulsa **Nueva clave** y repite el paso 9 |
| Tarea falla: `La rama base «main» no existe en el repositorio remoto.` | La rama por defecto del proyecto no existe | Corrígela en **Proyectos** (ShopyCube usa `master`) |
| Tarea falla en `git clone` / `push` con 403 | El token del worker no tiene *Contents: Read & write* | Paso 2 |
| La ejecución de Claude falla con un error de autenticación («Invalid API key» / «Invalid bearer token») | Credencial incorrecta, revocada o token de suscripción vencido | Genera otra (paso 3) y repite el script |
| La ejecución se detiene por límite de uso («usage limit reached») | Con suscripción, se agotó el límite de tu plan | Espera a que se renueve la ventana, o cambia a API key |

## Qué hace el worker en cada etapa de agente

1. `git clone` (una vez por repo) y `git fetch`; crea o reutiliza el worktree `work/task-{id}` en la rama `{prefijo}{id}-{slug}`
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
  SVG, ejecutables y cualquier otro formato se rechazan. Se guardan en la base de datos (columna `Tasks_Attachments.Content`),
  nunca en disco ni dentro del repositorio; las capturas de la pestaña «Capturas» también. Al arrancar, los adjuntos
  antiguos de `App_Data/attachments` se copian a la base de datos y se borran del disco. Si algún archivo no se
  encuentra, se conserva la columna `StoredName` (la única relación con el archivo) y se reintenta en el siguiente arranque.
  El HTML nunca se muestra en el dominio de la app: se descarga con `Content-Security-Policy: sandbox` y `nosniff`.
- **Markdown:** el HTML crudo se escapa y los enlaces sólo pueden ser http(s), mailto o relativos.
- **Workers:** cada PC tiene su propia clave (se guarda sólo el hash SHA-256), se puede deshabilitar o rotar,
  y una PC sólo puede leer y reportar sus propias ejecuciones.
- **Agentes:** el desarrollador usa `acceptEdits` con una lista blanca de comandos; el revisor no puede editar (`Edit`/`Write` prohibidos).
  Usa `bypassPermissions` sólo en VMs aisladas y desechables.

## Estructura

```
src/DevArv.Contracts   DTOs compartidos servidor ↔ worker (Models/)
src/DevArv.Web         Razor Pages, EF Core (Npgsql), Identity, API de workers, orquestador
  Api/                 Endpoints /api/worker y su filtro de autenticación
  Constants/           Plantillas de prompts y constantes
  DataModels/          Entidades EF Core (una clase por archivo)
  DatabaseConnection/  AppDbContext y DbInitializer (siembra del flujo por defecto)
  EnvironmentConfig/   EnvironmentData (conexión a la BD) y EnvironmentTools (carga y valida los appsettings)
  Enums/<Dominio>/     Enums por dominio: Workflow, Tasks, Runs
  Helpers/             Utilidades estáticas (Labels, Slug, FileSignature, MarkdownRenderer…)
  Models/              Opciones de configuración y DTOs internos
  Pages/<Feature>/     PageModels delgados + vistas
  Repositories/        Acceso a datos y reglas de negocio de cada pantalla
  Services/            WorkflowService, Orchestrator, GitHubService, AttachmentStorage, PromptBuilder
  ViewModels/          Modelos de formularios y de lectura de las páginas
src/DevArv.Worker      Servicio (Windows/systemd) que ejecuta Claude Code y git (EnvironmentConfig/, Helpers/, Models/, Services/)
scripts/               Configurar-Claves.ps1 (base de datos y claves en los appsettings.Local.json)
tests/DevArv.Tests     xUnit, con carpetas espejo de src: Web/EnvironmentConfig, Web/Helpers, Web/Services,
                       Worker/EnvironmentConfig, Worker/Helpers, Worker/Services
```

Las reglas de arquitectura y de organización del código están en [CLAUDE.md](CLAUDE.md).

## Próximos pasos sugeridos

- API pública con token para crear tareas desde otros sistemas (Jira, formularios).
- Webhooks de GitHub para detectar el merge del PR al instante (hoy se consulta cada minuto).
- Dependencias entre tareas, métricas de costo por proyecto y modo TV.
- Agente «Analista» antes de Aprobación Tarea que mejore la descripción y estime complejidad.
