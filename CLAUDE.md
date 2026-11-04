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

## Configuración: sólo appsettings

Toda la configuración de `DevArv.Web` y `DevArv.Worker` se lee de `appsettings.json` → `appsettings.{Entorno}.json` →
`appsettings.Local.json` (ignorado por git; ahí van las credenciales). `Program.cs` llama a
`builder.Configuration.UseAppSettingsOnly(...)` (`Helpers/AppSettingsConfiguration`), que quita user-secrets,
variables de entorno y línea de comandos.

- Cada ajuste nuevo se declara en `appsettings.json` (vacío si es un secreto) y en su clase `*Options` de `Models/`.
- No leas `Environment.GetEnvironmentVariable`, user-secrets ni archivos propios para obtener configuración o credenciales.
- Las credenciales no tienen respaldo: si faltan, se informa qué clave falta (no se recurre a la sesión de `claude`, al
  Credential Manager de git ni a variables de la PC).

## Organización de carpetas (proyecto `DevArv.Web`)

| Carpeta | Contenido |
|---|---|
| `Api/` | Endpoints `/api/worker` y `WorkerAuthFilter` |
| `Constants/` | Constantes y plantillas (`DefaultPrompts`, `PermissionModes`) |
| `DataModels/` | Entidades EF Core, **una clase por archivo** |
| `DatabaseConnection/` | `AppDbContext`, `DbInitializer` |
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
- `DevArv.Contracts` es el contrato HTTP entre servidor y worker: solo DTOs (`Models/`), sin dependencias. Cambiarlo exige actualizar Web y Worker a la vez.
- `DevArv.Worker` se organiza igual: `Helpers/` (sin estado), `Models/` (opciones y resultados), `Services/` (clases con dependencias).

## Pruebas

`tests/DevArv.Tests` replica la estructura de `src`: `Web/Helpers`, `Web/Services`, `Web/Repositories`, `Worker/Helpers`, `Worker/Services`. Un archivo de pruebas por clase, con el sufijo `Tests` (`SlugTests`, `PromptBuilderTests`).
