---
description: Revisión de Código Rigurosa — Quality Gate
model: claude-sonnet-5-5
effort: high
---

# Revisión de Código Rigurosa — Quality Gate

Realiza una revisión rigurosa y orientada a producción del código de la rama actual comparado con master.

Obtén los cambios con:

```
git diff master...HEAD
```

y también los cambios sin commitear:

```
git diff HEAD
```

El objetivo de esta revisión NO es generar la mayor cantidad posible de hallazgos.

El objetivo es determinar si la implementación:

1. Cumple realmente el requerimiento.
2. Respeta la arquitectura existente.
3. Es robusta ante NULL, datos inesperados y errores de ejecución.
4. Mantiene buenas prácticas de C#/.NET, SQL, EF Core, Dapper y TypeScript/JavaScript.
5. No introduce regresiones.
6. No filtra información entre tenants.
7. Es mantenible y adecuada para producción.

Detecta problemas reales que puedan:

- Romper la aplicación.
- Provocar excepciones en tiempo de ejecución.
- Corromper datos.
- Provocar pérdida de datos.
- Insertar valores NULL inválidos.
- Introducir regresiones.
- Violar la arquitectura existente.
- Violar las convenciones de nombrado del proyecto.
- Introducir vulnerabilidades de seguridad.
- Permitir acceso a datos de otro tenant.
- Provocar comportamiento incorrecto de negocio.
- Hacer significativamente más difícil el mantenimiento futuro.

Realiza la revisión como un ingeniero de software senior responsable de aprobar código para producción.

No apruebes código únicamente porque compila correctamente.

No reportes problemas especulativos sin evidencia concreta.

---

# 0. MODO DE TRABAJO

## Primera pasada — solo análisis

No modifiques archivos.

Analiza:

- Cambios de la rama.
- Requerimiento que se intenta resolver.
- Flujo completo de datos.
- NULL y nullability.
- Lógica de negocio.
- Arquitectura.
- SQL, EF Core y Dapper.
- Seguridad y multi-tenancy.
- Regresiones.
- Performance.
- Pruebas.

Entrega únicamente el reporte.

## Segunda pasada — solo con autorización explícita

Únicamente después de que el usuario autorice corregir:

- Corrige los hallazgos 🔴 y 🟠.
- No realices refactorizaciones no relacionadas.
- Mantén la arquitectura existente.
- Mantén el alcance del requerimiento.

Después de corregir:

1. Ejecuta nuevamente la revisión completa.
2. Confirma que los hallazgos fueron solucionados.
3. Confirma que no aparecieron problemas nuevos.
4. Vuelve a revisar NULL.
5. Confirma el cumplimiento del requerimiento.
6. Emite nuevamente la recomendación final de merge.

---

# 1. CONTEXTO DE LA REVISIÓN

Antes de identificar hallazgos, comprende el contexto del código.

Inspecciona la implementación existente y determina:

- Arquitectura actual.
- Requerimiento que se intenta resolver.
- Patrones de código existentes.
- Convenciones de nombrado.
- Implementaciones similares.
- Relaciones entre entidades y ViewModels.
- Patrones de repositorio.
- Patrones de validación.
- Restricciones de base de datos.
- Campos nullable y non-nullable.
- Helpers y abstracciones existentes.
- Patrones de inyección de dependencias.
- Convenciones para manejo de errores.
- Cómo estaba implementada previamente la funcionalidad.
- Pruebas existentes.

Sigue el flujo de datos desde la entrada hasta la persistencia y/o la respuesta.

La arquitectura actual del proyecto tiene prioridad sobre recomendaciones arquitectónicas genéricas.

NO recomiendes introducir una nueva arquitectura simplemente porque otra alternativa también sea válida.

Considera un cambio problemático cuando rompa, evite o vuelva inconsistente una arquitectura que ya haya sido establecida en la aplicación.

---

# 2. ALCANCE DE LA REVISIÓN

Revisa principalmente:

- Código nuevo.
- Código modificado.
- Código directamente afectado por los cambios.

Sin embargo, inspecciona código relacionado siempre que sea necesario para determinar si un cambio:

- Rompe un contrato existente.
- Introduce una regresión.
- Evita una validación existente.
- Viola la arquitectura.
- Produce un estado inválido en base de datos.
- Cambia comportamiento inesperadamente.
- Entra en conflicto con patrones existentes.
- Introduce problemas de seguridad o de aislamiento entre tenants.

No refactorices código no relacionado con los cambios.

No reportes problemas preexistentes que no estén relacionados con la rama revisada, salvo que los cambios actuales hagan que dichos problemas sean alcanzables o más peligrosos.

---

# 3. CLASIFICACIÓN DE HALLAZGOS

Todo hallazgo DEBE tener exactamente un nivel de severidad y exactamente un tipo.

## 3.1 Tipo de hallazgo

Cada hallazgo debe declarar explícitamente uno de estos tipos:

- **BUG REAL** — existe un camino de ejecución demostrable donde el código se comporta mal.
- **RIESGO REAL** — el código funciona hoy, pero una entrada, un estado o un consumidor razonable lo rompen.
- **MEJORA OPCIONAL** — no hay defecto; es mantenibilidad, legibilidad o consistencia.

Si no puedes clasificar un hallazgo en uno de estos tres tipos con evidencia, no lo reportes.

## 3.2 Niveles de severidad

### 🔴 CRÍTICO — BLOQUEADOR

La rama NO DEBE ser mezclada.

Usa este nivel cuando el problema pueda razonablemente provocar:

- Caída de la aplicación.
- Excepción no controlada (`NullReferenceException` u otra) en producción.
- Corrupción de datos.
- Pérdida de datos.
- Vulnerabilidad de seguridad.
- Bypass de autenticación.
- Bypass de autorización.
- Acceso a información de otro tenant/company.
- Operaciones de base de datos incorrectas.
- SQL que pueda fallar bajo condiciones normales.
- Inserción de NULL inválidos que pueda fallar en producción.
- Regresión severa.
- Cálculos financieros o de negocio críticos incorrectos.
- Funcionalidad inutilizable.
- Incumplimiento directo del requerimiento.
- Interrupción de producción.

Ejemplos:

- Acceder a una propiedad de un objeto que puede ser NULL.
- Insertar NULL en una columna obligatoria.
- Eliminar autorización de una operación protegida.
- Un query sin filtro de tenant.
- Actualizar o eliminar registros incorrectos.
- Exponer credenciales o información sensible.
- Una transacción incorrecta que pueda dejar datos inconsistentes.

Acción requerida:

> DEBE CORREGIRSE ANTES DE MEZCLAR

---

### 🟠 ALTO — DEBE CORREGIRSE

La rama no debería mezclarse hasta que el problema sea corregido.

Usa este nivel cuando exista un defecto importante que pueda producir:

- Comportamiento incorrecto.
- Regresiones importantes.
- Estados inconsistentes.
- Violación seria de arquitectura.
- Pérdida de validaciones importantes.
- Persistencia incompleta o incorrecta.
- Implementación parcial del requerimiento.
- Problema serio de nullability.
- Problema importante de concurrencia.
- Problema significativo de rendimiento.
- Falta de validación de datos de entrada.
- Falta de unit test en un método nuevo o modificado de un Repository (ver sección 16).
- Lógica de negocio en un PageModel (ver sección 7.1).

Ejemplos:

- Falta una validación obligatoria de negocio.
- Se omite una capa importante de la arquitectura.
- Mapeo incorrecto entre ViewModel y Entity.
- Un campo obligatorio no se persiste.
- Lógica condicional incorrecta.
- No se manejan casos esperados.
- Una actualización puede generar inconsistencia.
- Una nueva implementación evita un mecanismo central de validación.
- Se rompe un contrato importante existente.
- N+1 queries en un flujo con volumen real.

Acción requerida:

> DEBE CORREGIRSE ANTES DE MEZCLAR

---

### 🟡 MEDIO — DEBERÍA CORREGIRSE

El código puede potencialmente mezclarse, pero el hallazgo merece atención.

Usa este nivel para:

- Problemas de mantenibilidad.
- Duplicación significativa de lógica.
- Implementaciones frágiles.
- Convenciones de nombrado inconsistentes.
- Complejidad innecesaria.
- Validaciones incompletas donde el riesgo sea moderado.
- Estados no contemplados de impacto menor.
- Manejo de errores mejorable.
- Problemas menores de rendimiento.
- Código que pueda facilitar defectos futuros.

Acción requerida:

> DEBERÍA CORREGIRSE

No eleves preferencias puramente estilísticas a este nivel, salvo que violen una convención establecida del proyecto.

---

### 🔵 BAJO — RECOMENDACIÓN

Mejora opcional.

Usa este nivel con moderación para:

- Mejoras menores de legibilidad.
- Simplificaciones pequeñas.
- Limpieza no bloqueante.
- Pequeñas mejoras de nombrado.
- Consistencia menor.
- Refactorizaciones pequeñas con beneficio claro.

Acción requerida:

> OPCIONAL

No generes hallazgos de nivel BAJO únicamente para tener algo que reportar.

Una revisión sin hallazgos es completamente válida.

---

# 4. SEGURIDAD FRENTE A VALORES NULL — REVISIÓN OBLIGATORIA

Esta sección es prioritaria y obligatoria en TODA revisión.

Inspecciona explícitamente cada flujo de datos nuevo o modificado.

Sigue los valores a través de toda la cadena:

Entrada
→ ViewModel / DTO
→ Validación
→ Lógica de negocio
→ Repositorio
→ Entidad
→ Base de datos

Para cada valor que pueda ser NULL, determina si NULL es válido a lo largo de toda la cadena.

## 4.1 Orígenes que debes revisar

- Parámetros de métodos.
- Valores retornados por otros métodos.
- Objetos provenientes de base de datos.
- Tipos referencia nullable.
- Tipos valor nullable.
- Campos obligatorios y columnas NOT NULL.
- Propiedades de entidades, ViewModels y DTOs.
- Propiedades de navegación opcionales.
- Propiedades anidadas.
- Parámetros de Dapper.
- Inserts y updates con EF Core.
- Mapeos.
- `FirstOrDefault()` / `FirstOrDefaultAsync()`.
- `SingleOrDefault()` / `SingleOrDefaultAsync()`.
- `Find()`.
- LINQ en general.
- `LEFT JOIN` y campos SQL nullable.
- Diccionarios.
- Session, Claims, Request, QueryString, FormData.
- JSON y objetos deserializados.
- Respuestas de APIs.
- Colecciones posiblemente null o vacías.
- Configuración.

## 4.2 Patrones peligrosos a buscar específicamente

```csharp
object.Property            // cuando object puede ser NULL
object.Property.Value      // cuando Property puede ser NULL
collection.First()         // cuando la colección puede estar vacía
FirstOrDefault().Property  // sin validar el resultado
```

Y también:

```csharp
Single()
SingleOrDefault()
First()
Last()
ElementAt()
collection[0]
Dictionary[key]
```

cuando las condiciones necesarias para que funcionen pueden no cumplirse.

No te limites a los warnings del compilador. Determina si existe un **camino real de ejecución** donde un NULL provoque una excepción o comportamiento incorrecto.

## 4.3 Preguntas obligatorias por valor

1. ¿Este valor puede ser NULL?
2. Si puede ser NULL, ¿NULL es realmente válido?
3. ¿Puede llegar NULL a una columna NOT NULL?
4. ¿Puede provocar una excepción en tiempo de ejecución?
5. ¿Existe validación antes de persistir?
6. ¿La definición nullable de C# coincide con la base de datos?
7. ¿Se convierte un valor nullable en uno non-nullable sin validación?
8. ¿Un mapeo puede reemplazar silenciosamente un valor requerido por NULL?
9. ¿Un UPDATE puede eliminar accidentalmente un valor existente escribiendo NULL?

Nunca asumas que un valor no puede ser NULL únicamente porque la interfaz normalmente lo envía.

Valida los contratos reales del sistema.

Ejemplo:

```csharp
var Record = await Query.FirstOrDefaultAsync();
Record.Name = "Nuevo nombre";
```

Debes determinar si `Record` puede ser NULL.

También inspecciona patrones como:

```csharp
Entity.CurrencyID = Record.CurrencyID;
```

Si `Record.CurrencyID` es nullable pero `Entity.CurrencyID` o la columna de base de datos es obligatoria, verifica que exista una validación previa que garantice el valor.

Los problemas de NULL que puedan romper la aplicación o la persistencia deben clasificarse como 🔴 CRÍTICO o 🟠 ALTO según el impacto.

## 4.4 Entregable obligatorio

La revisión DEBE incluir la tabla de nullability descrita en la sección 19.

No finalices la revisión hasta haber recorrido todos los métodos nuevos o modificados buscando posibles NULL.

---

# 5. VALIDACIÓN DE PARÁMETROS

En todos los métodos nuevos o modificados revisa el comportamiento ante:

- `string` null.
- String vacío.
- Whitespace.
- IDs inválidos.
- IDs = 0.
- IDs inexistentes.
- Objetos null.
- Listas vacías.
- Fechas inválidas.
- Rangos de fechas incorrectos.
- Valores numéricos inválidos o fuera de rango.
- Enums con valores inválidos.

No asumas que un parámetro siempre tendrá un valor porque normalmente viene informado.

La implementación debe ser robusta ante datos inesperados, especialmente cuando el valor proviene del cliente.

---

# 6. BASE DE DATOS E INTEGRIDAD DE DATOS

Para todo INSERT y UPDATE revisa la integridad de datos.

Verifica:

- Que las columnas obligatorias reciban valores.
- Compatibilidad con NULL.
- IDs correctos.
- Foreign Keys válidas.
- Que valores existentes no sean sobrescritos accidentalmente.
- Que los mapeos estén completos.
- Que los tipos numéricos tengan precisión adecuada.
- Que las fechas utilicen la representación esperada.
- Que los enums contengan valores válidos.
- Que los valores por defecto sean intencionales.
- Que existan transacciones cuando la atomicidad sea necesaria.
- Que parámetros de Dapper coincidan exactamente con el SQL.
- Que EF Core coincida con las restricciones de base de datos.
- Que existan validaciones de duplicados cuando el negocio lo requiera.

## 6.1 SQL

Revisa:

- SQL inválido.
- JOIN incorrectos.
- `LEFT JOIN` que produzcan NULL no contemplados.
- WHERE incorrectos o incompletos.
- Filtro de tenant/company presente cuando corresponda.
- Parametrización correcta.
- SQL Injection.
- Duplicación de registros en el resultado.
- Concurrencia.
- Transacciones.
- Integridad referencial.
- Conversión de tipos.
- Manejo de fechas.
- Queries innecesariamente costosas.
- SELECT de columnas innecesarias.
- Índices potencialmente relevantes.

Presta especial atención a PostgreSQL.

## 6.2 EF Core

Revisa:

- `AsNoTracking()` cuando la consulta es de solo lectura.
- Proyección de únicamente las columnas necesarias.
- Problemas de traducción LINQ (evaluación en cliente).
- N+1 queries.
- `Include` innecesarios o faltantes.
- Que el ciclo de vida del `DbContext` se respete.

## 6.3 Dapper

Revisa:

- Parámetros y su correspondencia exacta con el SQL.
- Mapping de columnas a propiedades.
- Manejo de NULL.
- Resultados vacíos.
- Métodos que esperan un único resultado cuando pueden retornar cero (`QuerySingle` vs `QuerySingleOrDefault`).

Que el código compile correctamente NO significa que una operación de base de datos sea segura.

---

# 7. CUMPLIMIENTO DE ARQUITECTURA

El cumplimiento de arquitectura es obligatorio.

Antes de reportar una violación arquitectónica, inspecciona módulos similares para determinar el patrón establecido.

Verifica que los cambios respeten las responsabilidades existentes entre:

- Razor Pages.
- PageModels.
- Controllers.
- ViewModels.
- Lógica de negocio.
- Validación.
- Repositories.
- Services.
- EF Core.
- Dapper.
- Helpers.
- Entidades.
- Acceso a base de datos.
- TypeScript / JavaScript.
- SQL.

No permitas que una nueva implementación evite una capa existente si hacerlo:

- Duplica reglas de negocio.
- Duplica validaciones.
- Produce comportamiento inconsistente.
- Dificulta mantenimiento.
- Introduce lógica en una capa incorrecta.

Ejemplos problemáticos:

Incorrecto: Un PageModel implementa directamente lógica de base de datos cuando módulos equivalentes utilizan Repository.

Incorrecto: Un Repository introduce comportamiento específico de UI.

Incorrecto: Lógica de negocio importante implementada en JavaScript cuando pertenece al backend.

Incorrecto: Una regla de negocio se duplica en varias páginas en lugar de utilizar el mecanismo existente de validación.

Incorrecto: Se crea un nuevo Helper que duplica funcionalidad ya existente.

No muevas lógica entre capas por preferencia personal.

La consistencia arquitectónica del proyecto tiene prioridad sobre las preferencias personales del reviewer.

**Excepción:** las reglas explícitas de la sección 7.1 son estándar del proyecto, no preferencia personal, y prevalecen sobre el patrón de módulos existentes.

## 7.1 REGLA EXPLÍCITA — El PageModel NO contiene lógica de negocio

Un PageModel es la representación exclusiva de un controlador para una vista. Toda lógica de negocio vive en el Repository (o en el Helper/Service que este invoque), NUNCA en el PageModel.

Esta regla es un estándar explícito del proyecto. Que un módulo antiguo la incumpla (ej. un PageModel que genera la numeración y asigna campos del Record antes de llamar al Repository) NO justifica replicarlo en código nuevo.

**Lo que SÍ puede hacer un PageModel:**

- Declarar las propiedades que consume la vista y los parámetros de sus handlers (`[FromBody]`, `Guid ID`, etc.).
- Crear el contexto y los repositorios, y verificar permisos (`EnsurePermissionAsync` / `HasPermissionAsync`).
- Cargar listas y ViewModels de lectura llamando a métodos del Repository y asignar el resultado a las propiedades de la vista.
- Invocar el método del Repository (`InsertAsync`, `UpdateAsync`, `DeleteAsync`) pasando el ViewModel recibido tal cual.
- Traducir el resultado a `Page()`, `RedirectToPage(...)` o `JsonResult` (`success` / `message` / `id`) y manejar excepciones.
- Registrar el log de seguridad (convención existente del proyecto).

**Lo que NO puede hacer un PageModel (repórtalo como hallazgo):**

- Consultas EF Core, Dapper o SQL directo (`context.Algo.Where(...)`, `connection.QueryAsync(...)` escrito en el PageModel).
- Asignar o modificar campos del ViewModel/entidad recibido (`Record.DocID = ...`, `Record.DocType = ...`, fechas, estados, valores por defecto, IDs derivados).
- Generar numeración o consecutivos, calcular totales, estados o cantidades.
- Validar reglas de negocio (la validación pertenece al `Validate` del Repository).
- Decidir o derivar valores que se persisten (tipo de documento, flags, estados).
- Proyectar, filtrar o transformar datos para la respuesta (`.Select(...)` a objetos anónimos, joins o agrupaciones en memoria). El Repository devuelve el ViewModel listo y el PageModel lo serializa tal cual.
- Flujos de negocio de varios pasos o con reintentos (ej. generar un número con reintento y luego insertar). Eso es un único método del Repository.
- Llamar a varios métodos de escritura del Repository que deben ejecutarse juntos o en cierto orden.

**Cómo reportarlo:**

- 🟠 ALTO — DEBE CORREGIRSE ANTES DE MEZCLAR cuando la lógica está en código nuevo o modificado por el diff.
- Indica el archivo y la línea del PageModel y a qué método del Repository debe moverse.
- Si el diff mueve la lógica a un Repository, exige también el unit test correspondiente (sección 16).
- Código legacy que incumple la regla y el diff NO toca: no bloquees; menciónalo como 🔵 BAJO (deuda técnica) solo si es relevante para el cambio.

---

# 8. CONVENCIONES DE NOMBRADO DEL PROYECTO

Respeta las convenciones de nombrado existentes.

## 8.1 Parámetros de métodos

Los parámetros de métodos DEBEN utilizar PascalCase.

Correcto:

```csharp
public async Task UpdateAsync(
    CustomersViewModel Record,
    bool EditMode = false)
```

Incorrecto:

```csharp
public async Task UpdateAsync(
    CustomersViewModel record,
    bool editMode = false)
```

No recomiendes cambiar parámetros PascalCase a camelCase basándote en convenciones generales de Microsoft.

En este proyecto: las convenciones internas del sistema tienen prioridad sobre las convenciones genéricas.

## 8.2 Llamados a métodos: parámetros nombrados y en líneas separadas

Al invocar un método, los parámetros deben especificarse **nombrados y cada uno en su propia línea**, respetando la identación, a partir de estos umbrales:

| Tipo de llamada | Se aplica desde |
|---|---|
| Métodos de un `*Repository` | **2 o más parámetros** |
| Cualquier otra llamada (helpers, services, `HasPermissionAsync`, etc.) | **3 o más parámetros** |

Por debajo del umbral, la llamada puede escribirse en una sola línea y no debe reportarse como hallazgo.

Correcto — Repository con 2 parámetros:

```csharp
await Repository.GetCompanyByID(
    CompanyID: CompanyID,
    UserID: UserID);
```

Incorrecto — Repository con 2 parámetros en una sola línea:

```csharp
await Repository.GetCompanyByID(CompanyID, UserID);
```

Correcto — Repository con 1 parámetro:

```csharp
await Repository.GetCompanyByID(CompanyID: CompanyID);
```

Correcto — llamada que no es de Repository, con 2 parámetros:

```csharp
await Permissions.HasPermissionAsync(UserID, ModuleID);
```

Esta regla aplica en todo el código nuevo o modificado. Verifica explícitamente los llamados introducidos por la rama, comparando cada uno contra el umbral que le corresponde, y repórtalos como 🟡 MEDIO cuando no la cumplan.

## 8.3 Resto de elementos

Verifica también consistencia en:

- Clases.
- Interfaces.
- Métodos.
- Propiedades.
- Variables locales.
- Campos privados.
- Constantes.
- Enums.
- ViewModels y DTOs.
- Repositories.
- Objetos relacionados con base de datos.
- Helpers.
- Archivos y clases de TypeScript.
- IDs de HTML y selectores jQuery.

Antes de reportar un problema de naming, compara con implementaciones similares del proyecto.

No cambies nombres existentes innecesariamente; marca únicamente casos relevantes.

Los problemas de naming normalmente serán 🟡 MEDIO, salvo que provoquen ambigüedad funcional o errores.

---

# 9. VALIDACIONES Y LÓGICA DE NEGOCIO

Determina si las reglas de negocio están protegidas.

Verifica:

- Campos obligatorios.
- Campos condicionalmente obligatorios.
- Prevención de duplicados.
- Valores válidos de enums.
- Rangos numéricos.
- Relaciones entre campos.
- Reglas específicas por tipo de entidad.
- Validaciones específicas de UPDATE.
- Existencia del registro antes de UPDATE o DELETE.
- IDs recibidos externamente.
- Validación antes de persistir.

No dependas exclusivamente de validaciones del frontend.

Toda regla que proteja persistencia o integridad debe estar validada en backend.

No te limites a la sintaxis. Comprueba:

- Condiciones `if/else` y `switch`.
- Estados.
- Permisos y roles.
- Casos límite.
- Fechas.
- Cálculos y totales.
- Filtros.
- Paginación.
- Ordenamiento.
- Duplicados.
- Casos sin resultados.

Para cada flujo pregunta:

- ¿Qué pasa si este dato no existe?
- ¿Qué pasa si esta colección está vacía?
- ¿Qué pasa si este valor es NULL?
- ¿Qué pasa si el usuario no tiene permisos?
- ¿Qué pasa si la base de datos no devuelve registros?
- ¿Qué pasa si el usuario envía un valor inesperado?
- ¿Qué pasa si este método falla?
- ¿Qué pasa si se ejecuta dos veces?

---

# 10. OPERACIONES UPDATE

Los UPDATE requieren revisión adicional.

Busca posibles pérdidas accidentales de información.

Para cada UPDATE pregunta:

- ¿Un valor faltante del ViewModel puede sobrescribir información existente con NULL?
- ¿Los campos excluidos del UPDATE lo están intencionalmente?
- ¿Se actualiza la entidad correcta?
- ¿Se valida que el registro exista?
- ¿Se valida que el registro pertenezca al tenant y al usuario actual?
- ¿Los campos inmutables están protegidos?
- ¿Se confía en un ID externo sin validación?
- ¿La validación de duplicados excluye correctamente el registro actual?
- ¿Se modifican campos no relacionados?
- ¿Existe riesgo de concurrencia?
- ¿Los updates parciales conservan los valores existentes?

Ejemplo peligroso:

```csharp
Entity.Phone = Record.Phone;
Entity.CurrencyID = Record.CurrencyID;
Entity.Name = Record.Name;
```

Si alguno de estos valores puede llegar NULL por ausencia en el formulario o ViewModel, determina si se pretende borrar el valor o si el UPDATE está destruyendo información accidentalmente.

---

# 11. OPERACIONES INSERT

Para INSERT verifica:

- Todos los campos obligatorios.
- Validación antes de persistir.
- Columnas NOT NULL.
- Defaults.
- Relaciones.
- Foreign Keys.
- IDs.
- Asignación correcta del tenant/company.
- Duplicados.
- Valores por defecto de enums.
- Campos obligatorios según reglas condicionales.

Ejemplo:

Si un enum tiene:

```csharp
NOT_SPECIFIED = 0
```

pero el negocio exige que el usuario seleccione un valor, verifica que `0` no pueda persistirse silenciosamente.

---

# 12. SEGURIDAD Y MULTI-TENANCY

Revisa el código modificado buscando:

- Falta de autorización.
- Control de acceso incorrecto.
- SQL Injection.
- SQL dinámico inseguro.
- XSS.
- IDOR y manipulación de IDs.
- Credenciales en código.
- Información sensible en logs.
- Exposición de excepciones al usuario.
- Inputs sin validar.
- Mass assignment.
- Acceso a registros de otros usuarios.
- Falta de validación de propiedad o permisos.
- Redirects inseguros.
- Manejo inseguro de archivos.

## 12.1 Multi-tenancy — revisión obligatoria

El sistema utiliza lógica multi-tenant.

Verifica explícitamente que ninguna consulta, INSERT, UPDATE o DELETE nuevo o modificado permita acceder o afectar información de otro tenant/company.

En particular:

- Todo SELECT sobre datos de negocio debe filtrar por el tenant actual.
- Todo UPDATE/DELETE por ID debe validar además la pertenencia al tenant.
- Un ID recibido del cliente nunca es prueba de propiedad.

Un fallo de aislamiento entre tenants es siempre 🔴 CRÍTICO.

Las vulnerabilidades explotables deben clasificarse normalmente como 🔴 CRÍTICO.

---

# 13. CÓDIGO ASÍNCRONO Y PERFORMANCE

## 13.1 Async

Revisa código async buscando:

- Falta de `await`.
- Firmas async incorrectas.
- Uso de `.Result`.
- Uso de `.Wait()`.
- Operaciones fire-and-forget cuando importa su finalización.
- Acceso síncrono innecesario a base de datos.
- Uso incorrecto del ciclo de vida de `DbContext`.
- Recursos no liberados correctamente.

No recomiendes convertir código a async si no existe un beneficio real.

## 13.2 Performance

Busca:

- Queries innecesarias.
- N+1 queries.
- Enumeraciones múltiples de un `IEnumerable`.
- Consultas dentro de loops.
- Llamadas HTTP dentro de loops.
- Carga de objetos completos cuando solo se necesitan algunas columnas.
- Falta de `AsNoTracking()` en consultas de solo lectura.
- Operaciones síncronas innecesarias.
- Procesamiento innecesario en memoria que la base de datos podría resolver.

No optimices prematuramente. Marca únicamente problemas con impacto razonable en volúmenes reales.

---

# 14. MANEJO DE ERRORES

Verifica:

- Manejo de fallos esperados.
- Resultados vacíos y datos inexistentes.
- Errores de base de datos.
- Errores de API.
- Parsing y deserialización.
- Timeouts.
- Excepciones no silenciadas.
- Excepciones que oculten defectos reales.
- Mensajes de error con contexto suficiente.
- Que no se exponga información sensible.
- Uso apropiado de validaciones frente a excepciones.

No recomiendes simplemente:

```csharp
try
{
}
catch
{
}
```

sin resolver la causa.

Si existe un `catch`, determina si:

- Está justificado.
- Registra correctamente el error.
- Devuelve una respuesta adecuada.
- Permite continuar de forma segura.

Evita recomendar código defensivo que simplemente oculte un estado inválido.

Prefiere proteger las invariantes en la capa correcta.

---

# 15. FRONTEND (TypeScript / JavaScript)

Si la rama contiene cambios de frontend, revisa:

- `null` / `undefined`.
- Elementos HTML inexistentes al momento de la consulta.
- Manejo de respuestas HTTP no exitosas.
- JSON inválido.
- Propiedades inexistentes en la respuesta.
- Manejo de errores.
- Estados de carga.
- Requests duplicados.
- Race conditions.
- Validación de inputs.
- Uso correcto de `fetch`.
- Correspondencia entre lo que envía el backend y lo que espera el frontend.

Respeta los patrones existentes del proyecto, incluyendo utilidades ya establecidas como:

```typescript
Tools.fetchWithLoadingIcon(...)
```

No reemplaces patrones existentes sin una razón técnica clara.

Recuerda que la validación de frontend nunca sustituye la validación de backend.

---

# 16. REVISIÓN DE PRUEBAS

Revisa las pruebas relacionadas con los cambios.

## Regla obligatoria: pruebas unitarias en Repositories

Siempre que se agregue un método nuevo a cualquier Repository del proyecto, o se modifique un método ya existente en cualquier Repository, debe existir un unit test que cubra dicho método.

Si detectas un método nuevo o modificado en un Repository sin el unit test correspondiente, repórtalo como hallazgo 🟠 ALTO, salvo que se trate de un wrapper trivial sin lógica propia (por ejemplo, un passthrough directo sin ninguna condición, transformación o regla de negocio).

Verifica además:

- Que reglas importantes de negocio tengan pruebas cuando corresponda.
- Que las pruebas validen comportamiento real.
- Que no prueben únicamente detalles internos de implementación.
- Que casos de borde relevantes tengan cobertura.
- Que las pruebas existentes no hayan sido debilitadas únicamente para hacerlas pasar.
- Que una nueva regla de negocio importante tenga una prueba razonable.

No exijas pruebas para código trivial y de riesgo insignificante.

Prioriza pruebas de:

- Lógica de negocio.
- Validaciones.
- Regresiones.
- Casos límite.
- Código con alto riesgo de producción.

---

# 17. ANÁLISIS DE REGRESIONES

No revises las líneas modificadas de forma aislada.

Determina qué funcionaba anteriormente y cómo los cambios pueden afectar ese comportamiento.

Busca:

- Cambios de contratos.
- Cambios en valores retornados.
- Cambios de nullability.
- Cambios de defaults.
- Validaciones eliminadas.
- Mapeos eliminados.
- Filtros modificados.
- Ordenamientos modificados.
- Cambios de paginación.
- Cambios de autorización.
- Cambios de comportamiento de base de datos.
- Cambios en enums.
- Cambios de comportamiento en métodos compartidos.

Determina si los cambios pueden romper:

- Funcionalidades existentes.
- Consumidores del método modificado.
- Controllers y PageModels.
- Repositories.
- APIs.
- Jobs y procesos automáticos.
- Multi-tenancy.
- Permisos.
- Integraciones.

Presta especial atención a cambios en métodos existentes que alteren su comportamiento para los consumidores actuales. Inspecciona esos consumidores cuando sea necesario.

---

# 18. CUMPLIMIENTO DEL REQUERIMIENTO

Responde explícitamente:

### ¿El requerimiento está realmente cumplido?

Clasifica como:

- ✅ **COMPLETO**
- ⚠️ **PARCIAL**
- ❌ **NO CUMPLE**

Explica exactamente por qué.

Si es parcial, indica qué falta.

Si está completo, comprueba el flujo de extremo a extremo y no únicamente que compile.

Si el requerimiento no puede determinarse a partir de la rama (nombre, commits, ticket, descripción del usuario), indícalo explícitamente en lugar de asumirlo.

---

# 19. EVITAR FALSOS POSITIVOS

Una revisión rigurosa NO significa generar muchos comentarios.

NO reportes:

- Preferencias personales de estilo.
- Implementaciones alternativas que sean simplemente diferentes.
- Optimización prematura.
- Problemas hipotéticos poco realistas.
- Problemas preexistentes no relacionados (excepción: la regla de la sección 7.1 aplica siempre a código nuevo o modificado, aunque módulos existentes la incumplan).
- Recomendaciones genéricas que contradigan las convenciones del proyecto.
- Refactorizaciones sin beneficio concreto.
- Comentarios que un desarrollador competente razonablemente no implementaría.

Cada hallazgo debe responder claramente: **¿qué problema concreto puede provocar esto y bajo qué escenario?**

Si no puedes responderlo con evidencia, no reportes el hallazgo.

## Orden de prioridad

1. Bugs.
2. Excepciones potenciales.
3. NULL.
4. Seguridad y multi-tenancy.
5. Cumplimiento del requerimiento.
6. Regresiones.
7. Arquitectura.
8. Performance.
9. Mantenibilidad.
10. Estilo.

---

# 20. EJECUCIÓN OBLIGATORIA DEL SKILL code-naming

Como último paso del análisis, ANTES de mostrar el reporte final, invoca el skill `code-naming` sobre el mismo código revisado.

Incorpora los hallazgos de nombrado que produzca `code-naming` como hallazgos adicionales de esta misma revisión, clasificándolos con el sistema de severidad de la sección 3 (normalmente 🟡 MEDIO, salvo que provoquen ambigüedad funcional o errores, según la sección 8).

Incluye en esta verificación la regla de **un parámetro nombrado por línea** en los llamados a métodos, respetando los umbrales de la sección 8.2 (Repository desde 2+ parámetros; cualquier otra llamada desde 3+).

No omitas este paso aunque no se detecten violaciones de nombrado.

---

# 21. FORMATO OBLIGATORIO DEL REPORTE

El reporte final DEBE seguir exactamente esta estructura.

## 📋 RESUMEN DE LA REVISIÓN

**Cumplimiento del requerimiento:** ✅ COMPLETO / ⚠️ PARCIAL / ❌ NO CUMPLE

**Hallazgos:**
- 🔴 Críticos: X
- 🟠 Altos: X
- 🟡 Medios: X
- 🔵 Bajos: X

---

## 🔎 HALLAZGOS

Para cada hallazgo:

### [COLOR] [SEVERIDAD] — Título corto

**Ubicación:**
`Archivo.cs:línea` o `Archivo.cs → Método()`

**Tipo:**
BUG REAL / RIESGO REAL / MEJORA OPCIONAL

**Problema:**
Describe exactamente qué está mal.

**Escenario que lo provoca:**
Describe qué tendría que ocurrir para que el problema se manifieste. Debe ser un camino de ejecución realista.

**Impacto:**
Explica el impacto concreto en producción:
- Runtime.
- Datos.
- Arquitectura.
- Seguridad / multi-tenancy.
- Negocio.
- Mantenimiento.

**Evidencia:**
Explica por qué el problema aplica específicamente a este código.

Cuando corresponda referencia:
- Implementación relacionada.
- Base de datos o constraint.
- Caller afectado.
- Arquitectura existente.
- Convención del proyecto.
- Código similar en el proyecto.

**Corrección recomendada:**
Propón la corrección mínima razonable, respetando la arquitectura existente.

**Código sugerido:**
Incluye únicamente el fragmento necesario para corregirlo.

### Ejemplo

### 🔴 CRÍTICO — Campo obligatorio puede persistirse como NULL

**Ubicación:** `CustomersRepository.cs:84`

**Tipo:** BUG REAL

**Problema:**
`CurrencyID` se asigna directamente desde `CustomersViewModel.CurrencyID`. La propiedad del ViewModel es nullable, mientras que la entidad y la columna de base de datos requieren un valor. No existe validación en este flujo que garantice el valor antes de persistir.

**Escenario que lo provoca:**
El usuario envía el formulario sin seleccionar moneda, o un request llega sin ese campo.

**Impacto:**
La persistencia se ejecuta con `CurrencyID = null`, provocando una excepción de base de datos y fallando la operación en producción.

**Evidencia:**
El ViewModel permite NULL mientras que el contrato de persistencia no lo permite.

**Corrección recomendada:**
Agregar la validación correspondiente utilizando el mecanismo de validación existente del proyecto (`Validate()`) antes de ejecutar la persistencia. No implementar un workaround aislado en el Repository si la arquitectura ya centraliza estas reglas.

---

## 🛡️ ANÁLISIS DE NULLABILITY

Incluye siempre esta sección, aunque no existan hallazgos.

| Ubicación | Valor | ¿Puede ser NULL? | Origen / Por qué | Riesgo | Acción |
|---|---|---|---|---|---|
| `Archivo.cs:línea` | `Record.CurrencyID` | Sí | ViewModel nullable sin validación | 🔴 | Validar en `Validate()` |

Debe listar todos los valores relevantes de los métodos nuevos o modificados, incluidos los que resultaron seguros (marcados como "No" y riesgo ✅), para demostrar que el barrido fue exhaustivo.

---

## ✅ CHECKLIST FINAL

Muestra este checklist marcado en el reporte:

- [ ] Todos los parámetros fueron revisados.
- [ ] Todos los posibles NULL fueron revisados.
- [ ] No existe acceso inseguro a propiedades posiblemente NULL.
- [ ] No existen `First()` / `Single()` / `Last()` inseguros.
- [ ] No existen accesos `[0]` inseguros.
- [ ] No existen `Dictionary[key]` inseguros.
- [ ] Las colecciones vacías están contempladas.
- [ ] Los resultados vacíos de base de datos están contemplados.
- [ ] Las operaciones INSERT fueron revisadas.
- [ ] Las operaciones UPDATE fueron revisadas (pérdida accidental de datos).
- [ ] Los mapeos fueron revisados.
- [ ] Las reglas de negocio y validaciones fueron revisadas.
- [ ] Los parámetros de métodos utilizan PascalCase.
- [ ] Los llamados a métodos usan parámetros nombrados, uno por línea (Repository desde 2+ parámetros; resto desde 3+).
- [ ] La arquitectura existente se respeta.
- [ ] La lógica de negocio está en la capa correcta.
- [ ] Ningún PageModel nuevo o modificado contiene lógica de negocio: sin consultas, sin asignar campos del Record, sin numeración, validación ni transformación de datos (sección 7.1).
- [ ] SQL fue revisado.
- [ ] EF Core / Dapper fueron revisados.
- [ ] Multi-tenancy fue revisado.
- [ ] Autorización fue revisada.
- [ ] Frontend fue revisado si aplica.
- [ ] Manejo de excepciones fue revisado.
- [ ] Código async fue revisado.
- [ ] Posibles regresiones y consumidores afectados fueron revisados.
- [ ] Performance fue revisado.
- [ ] Existen unit tests para los métodos nuevos o modificados de Repositories.
- [ ] El skill `code-naming` fue ejecutado.
- [ ] El requerimiento fue validado de extremo a extremo.

---

# 22. RECOMENDACIÓN FINAL DE MERGE

NUNCA omitas esta sección.

Antes de emitirla, responde internamente:

1. ¿Existe algún camino razonable donde un valor NULL pueda romper la aplicación?
2. ¿Existe algún INSERT que pueda persistir datos inválidos?
3. ¿Existe algún UPDATE que pueda destruir o reemplazar información incorrectamente?
4. ¿Se violó una restricción de base de datos?
5. ¿Se rompió alguna regla de negocio?
6. ¿Se está ignorando la arquitectura existente?
7. ¿Se introdujo una vulnerabilidad o una fuga entre tenants?
8. ¿Existe una regresión funcional?
9. ¿Se rompió algún contrato consumido por otro código?
10. ¿El requerimiento está realmente cumplido?
11. ¿Existe algún hallazgo 🔴 CRÍTICO o 🟠 ALTO?

Luego emite exactamente uno de estos tres estados, según el mapeo determinista:

## 🔴 NO RECOMIENDO MERGE

Usar cuando exista al menos un hallazgo 🔴 CRÍTICO, o cuando el requerimiento sea ❌ NO CUMPLE.

> 🔴 NO RECOMIENDO MERGE
> Se identificaron problemas críticos que deben ser corregidos antes de mezclar esta rama.

Después explica brevemente qué hallazgos bloquean la mezcla.

## 🟡 SÍ, PERO DESPUÉS DE CORREGIR LOS HALLAZGOS

Usar cuando no existan 🔴 CRÍTICOS pero exista al menos un 🟠 ALTO, o cuando el requerimiento sea ⚠️ PARCIAL.

> 🟡 SÍ, PERO DESPUÉS DE CORREGIR LOS HALLAZGOS
> No existen hallazgos críticos, pero se identificaron problemas de nivel Alto que deben corregirse antes de mezclar.

Después explica brevemente qué hallazgos bloquean la mezcla.

## 🟢 SÍ, RECOMIENDO MERGE

Usar únicamente cuando se cumplan TODAS estas condiciones:

- 0 hallazgos 🔴 CRÍTICOS.
- 0 hallazgos 🟠 ALTOS.
- Requerimiento ✅ COMPLETO.
- No existe evidencia de que la aplicación pueda romperse.
- No existen problemas conocidos de integridad de datos.
- No existe violación arquitectónica bloqueante.
- No existe problema de seguridad ni de multi-tenancy bloqueante.

> 🟢 SÍ, RECOMIENDO MERGE
> No se identificaron problemas bloqueantes. La rama no contiene hallazgos conocidos de nivel Crítico o Alto que deban impedir su mezcla.
> Los hallazgos Medios o Bajos indicados anteriormente son recomendaciones y no bloquean la mezcla.

Si no existe ningún hallazgo:

> 🟢 SÍ, RECOMIENDO MERGE
> No se identificaron hallazgos accionables durante la revisión.
> Según los cambios inspeccionados, la rama es apta para ser mezclada.

No inventes hallazgos para justificar una decisión.

La aprobación debe estar basada exclusivamente en evidencia encontrada durante la revisión.

---

# 23. FILOSOFÍA DE REVISIÓN

Opera bajo estos principios:

- Corrección > estilo.
- Integridad de datos > conveniencia.
- Aislamiento entre tenants > cualquier optimización.
- Arquitectura existente > preferencia del reviewer.
- Convenciones del proyecto > convenciones genéricas.
- Evidencia > especulación.
- Riesgo real de producción > problemas teóricos.
- Corrección mínima y segura > refactorización innecesaria.

El responsable de la revisión debe detectar código peligroso, pero también debe evitar bloquear una rama por preferencias personales o problemas irrelevantes.
