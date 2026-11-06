# DevArv — Estándares de desarrollo

## Arquitectura: el PageModel NO contiene lógica de negocio

Un PageModel es la representación exclusiva de un controlador para una vista. **Toda lógica de negocio y de acceso a datos vive en el Repository** (o en el Service/Helper que este invoque), nunca en el PageModel.

**Un PageModel solo puede:**

- Declarar las propiedades de la vista y los parámetros de sus handlers.
- Cargar listas y ViewModels de lectura llamando al Repository.
- Invocar `SaveAsync` / `InsertAsync` / `DeleteAsync` (o un método de `WorkflowService`) pasando el ViewModel recibido tal cual.
- Traducir el resultado a `Page()`, `RedirectToPage(...)`, `Redirect(...)` o `JsonResult`, y manejar `ValidationFailedException` / `WorkflowException` (copiar los errores a `ModelState` o `TempData`).

**Un PageModel NO puede:**

- Hacer consultas EF Core ni usar `AppDbContext`.
- Asignar o modificar campos del ViewModel o de la entidad (valores por defecto, fechas, estados, trim/normalización).
- Validar reglas de negocio (van en el Repository; las anotaciones de DataAnnotations en el ViewModel sí se permiten).
- Proyectar o transformar datos para la vista: el Repository devuelve el ViewModel listo.
- Ejecutar flujos de varios pasos (crear tarea + guardar adjuntos + entrar a una columna): es un único método del Repository o de `WorkflowService`.

Ejemplos correctos: `Pages/Tasks/Create.cshtml.cs` (llama a `WorkflowService.CreateTaskAsync`), `Pages/Projects/Edit.cshtml.cs` y `Pages/Workflow/Edit.cshtml.cs` (llaman a `SaveAsync` de su Repository).

Si encuentras código que no cumple, se migra al Repository cuando se modifique; no se replica en código nuevo.

### Errores de validación

Los Repositories y Services lanzan `ValidationFailedException` (campo + mensaje, por ejemplo `"Input.Name"`) para reglas de negocio que el usuario puede corregir, y `WorkflowException` para transiciones inválidas del flujo. El PageModel los captura y los muestra; nunca decide la regla.

## Configuración: `EnvironmentConfig` (igual que ShopyCube)

`DevArv.Web` y `DevArv.Worker` tienen la carpeta `EnvironmentConfig/` con dos clases, como ShopyCube:

- **`EnvironmentTools`** (estática): única fuente de configuración. Carga una sola vez `appsettings.json` →
  `appsettings.{Entorno}.json` → `appsettings.Local.json` (ignorado por git; ahí van las credenciales) y valida los datos
  con `ArgumentException`, indicando la clave (`"EnvironmentData:Host no puede estar vacío."`). `Program.cs` llama a
  `EnvironmentTools.UseAsConfigurationSource(builder.Configuration)`, que quita user-secrets, variables de entorno y
  línea de comandos, así que las `*Options` también salen de los appsettings.
- **`EnvironmentData`**: datos de conexión de la sección `EnvironmentData`, leídos con `EnvironmentTools.GetData()`.
  - Web: base de datos (`Host`, `Port`, `Database`, `Username`, `Password`) y `GetConnectionString()`.
  - Worker: servidor DevArv (`Url`, `ApiKey`) y `GetBaseAddress()`.

Reglas:

- Los datos de conexión se leen con `EnvironmentTools.GetData()`; el resto de ajustes, con su clase `*Options` de
  `Models/`, declarada también en `appsettings.json` (vacía si es un secreto).
- Sólo `EnvironmentTools` puede llamar a `Environment.GetEnvironmentVariable` (para saber el entorno y, en el worker, el
  `PATH`/`PATHEXT` con que se ubican `git` y `claude`: `GetExecutableSearchDirectories`). No leas
  user-secrets, variables de entorno ni archivos propios para obtener configuración o credenciales.
- Si falta un dato o una credencial la app no arranca y el mensaje indica qué clave falta; no hay respaldo en la sesión
  de `claude`, el Credential Manager de git ni variables de la PC.

## Organización de carpetas (proyecto `DevArv.Web`)

| Carpeta | Contenido |
|---|---|
| `Api/` | Endpoints `/api/worker` y `WorkerAuthFilter` |
| `Constants/` | Constantes y plantillas (`DefaultPrompts`, `PermissionModes`) |
| `DataModels/` | Entidades EF Core, **una clase por archivo** |
| `DatabaseConnection/` | `AppDbContext`, `DbInitializer` |
| `EnvironmentConfig/` | `EnvironmentData` (datos de conexión) y `EnvironmentTools` (carga y valida los appsettings) |
| `Enums/<Dominio>/` | **Un enum por archivo**, agrupados por dominio (`Workflow`, `Tasks`, `Runs`) |
| `Helpers/` | Utilidades sin estado (`Labels`, `Slug`, `FileSignature`, `MarkdownRenderer`, `ApiKeys`, `Clock`…) |
| `Models/` | Opciones de configuración (`*Options`) y DTOs internos |
| `Pages/<Feature>/` | `Index`, `Create`, `Edit`, `Details`… (PageModel + vista) y `Pages/Shared/` |
| `Repositories/` | `<Entidad>Repository`: consultas, validación y persistencia, con `AppDbContext` por DI (scoped) |
| `Services/` | Lógica transversal o con estado externo: `WorkflowService`, `Orchestrator`, `GitHubService`, `AttachmentStorage`, `PromptBuilder` |
| `ViewModels/` | Modelos de formulario y lectura, sufijo `ViewModel` |

Reglas:

- **Un tipo público por archivo**, con el nombre del archivo igual al del tipo. Las excepciones propias van junto al servicio o repository que las lanza.
- Los namespaces siguen la estructura de carpetas (`DevArv.Web.Repositories`, `DevArv.Web.Enums.Tasks`…).
- Cada Repository nuevo se registra en `Program.cs` como `AddScoped`.
- `DevArv.Contracts` es el contrato HTTP entre servidor y worker: solo DTOs (`Models/`), sin dependencias. Cambiarlo exige actualizar Web y Worker a la vez. Los miembros siguen el nombrado del código (`RunID`), pero el nombre del JSON se fija con `[JsonPropertyName("runId")]`: los workers instalados en otras PCs pueden estar en otra versión. `WireFormatTests` congela esos nombres.
- `DevArv.Worker` se organiza igual: `EnvironmentConfig/`, `Helpers/` (sin estado), `Models/` (opciones y resultados), `Services/` (clases con dependencias).

## Base de datos y nombrado

El nombrado del código sigue el skill `code-naming` (parámetros y `ID` en PascalCase/mayúsculas, constantes y miembros de enum en `UPPER_SNAKE_CASE`, campos privados `_Pascal`, llamadas con parámetros nombrados). Para no romper las bases ya creadas:

- **Tablas**: `Modulo_Tabla` (`Tasks_Tasks`, `Workflow_Stages`, `Identity_Users`…). Los nombres viven en `DatabaseConnection/TableNames.cs`; el modelo, `DbInitializer` y el SQL escrito a mano usan esas constantes. Las bases creadas con los nombres antiguos se renombran solas al arrancar (`DbInitializer.TABLE_RENAMES`, un bloque atómico e idempotente).
- **Columnas**: en el código la palabra es `ID` (`TaskID`), pero la columna conserva su nombre histórico (`TaskId`); `AppDbContext` hace ese mapeo en un solo lugar. No hace falta renombrar columnas.
- **Enums**: el código usa `WAITING_APPROVAL`; la base guarda el texto `WaitingApproval` (`EnumNameStringConverter`). No cambies los miembros de un enum sin revisar ese texto: está guardado en las filas existentes. Una prioridad no se ordena por su columna (es texto): usa el rango explícito de `TasksRepository.GetBoardAsync`.
- **SQL escrito a mano**: solo `DbInitializer` y el bloqueo `FOR UPDATE` de `TasksRepository`. El proveedor en memoria de las pruebas no ejecuta SQL, así que cualquier cambio ahí hay que probarlo contra PostgreSQL.
- **PageModels**: no se usa `[BindProperty]`. Los handlers reciben sus datos como parámetros (`OnPostAsync(int? ID, XxxViewModel Input, …)`) y asignan las propiedades de solo lectura que consume la vista.

## Pruebas

`tests/DevArv.Tests` replica la estructura de `src`: `Web/DatabaseConnection`, `Web/Helpers`, `Web/Services`, `Web/Repositories`, `Worker/Helpers`, `Worker/Services`. Un archivo de pruebas por clase, con el sufijo `Tests` (`SlugTests`, `PromptBuilderTests`).
