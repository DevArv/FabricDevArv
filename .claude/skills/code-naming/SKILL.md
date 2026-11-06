---
name: code-naming
description: Estándar de nombrado del proyecto para C#/.NET, Razor Pages, TypeScript, jQuery, CSS/HTML y PostgreSQL. Úsalo siempre que se escriba, revise, refactorice o genere código nuevo en el proyecto (clases, métodos, variables, parámetros, constantes, enums, archivos .ts, IDs de HTML, selectores jQuery, o nombres de tablas de base de datos), o cuando el usuario pregunte cómo se debe nombrar algo, pida una code review enfocada en convenciones de nombrado, o mencione "estándar de nombrado", "naming convention" o "convenciones de nombres".
---

# Estándar de Nombrado del Proyecto

Stack: ASP.NET Core Razor Pages, C#, TypeScript, jQuery, CSS, HTML, PostgreSQL.

Aplica estas reglas al escribir código nuevo, al revisar código existente, o al responder preguntas sobre cómo nombrar algo en este proyecto. Si el código que estás generando o revisando viola alguna de estas reglas, corrígelo o señálalo explícitamente.

---

## 1. C# / .NET (Backend)

| Elemento | Convención | Ejemplo |
|---|---|---|
| Clases | PascalCase | `CompanyService`, `InvoiceRepository` |
| Interfaces | PascalCase con prefijo `I` | `ICompanyService` |
| Métodos | PascalCase | `GetCompanyByID()` |
| Propiedades públicas | PascalCase | `public string CompanyName { get; set; }` |
| Variables locales | camelCase | `var companyID = 1;` |
| Parámetros de método | **PascalCase** | `GetCompany(int CompanyID)` |
| Campos privados | **PascalCase con prefijo `_`** | `private readonly ILogger _Logger;` |
| Constantes | **UPPER_SNAKE_CASE** | `const int MAX_RETRIES = 3;` |
| Enums (miembros) | **UPPER_SNAKE_CASE** (el nombre del tipo enum se mantiene en PascalCase) | `enum InvoiceStatus { PENDING, APPROVED }` |
| Namespaces | PascalCase, siguiendo estructura de carpetas | `App.Services.Billing` |

### 1.1 Razor Pages específico

| Elemento | Convención | Ejemplo |
|---|---|---|
| Carpeta de página | PascalCase | `/Pages/Companies/` |
| Archivo `.cshtml` | PascalCase | `Index.cshtml`, `Edit.cshtml` |
| PageModel (`.cshtml.cs`) | PascalCase + sufijo `Model` | `IndexModel`, `EditModel` |
| Handlers (`OnGet`, `OnPost`) | PascalCase + sufijo descriptivo | `OnPostDeleteAsync()` |
| ViewModels / DTOs | PascalCase + sufijo `ViewModel` o `Dto` | `CompanyEditViewModel`, `CompanyDto` |

No se utiliza `[BindProperty]` en este proyecto.

---

## 2. TypeScript (Frontend)

| Elemento | Convención | Ejemplo |
|---|---|---|
| Archivos | **PascalCase (igual que C#)** | `CompanyList.ts` |
| Clases / Interfaces | PascalCase | `class CompanyTable {}` |
| Funciones | camelCase | `function loadCompanies() {}` |
| Variables | camelCase | `let totalCount = 0;` |
| Constantes globales | UPPER_SNAKE_CASE | `const MAX_PAGE_SIZE = 50;` |
| Enums (miembros) | **UPPER_SNAKE_CASE** (el nombre del tipo enum se mantiene en PascalCase) | `enum Status { ACTIVE, INACTIVE }` |
| Tipos genéricos | Una letra mayúscula o PascalCase descriptivo | `T`, `TResponse` |

---

## 3. CSS / HTML

| Elemento | Convención | Ejemplo |
|---|---|---|
| Clases CSS | kebab-case (metodología BEM sugerida) | `.company-card`, `.company-card__title`, `.company-card--active` |
| **ID de campos ligados a una propiedad del modelo** | **`@nameof(PropiedadDelModelo)`** | `id="@nameof(Model.CompanyName)"` |
| ID de elementos que NO representan una propiedad del modelo | kebab-case | `id="main-header"` |
| Archivos CSS | kebab-case | `company-list.css` |
| Atributos `data-*` | kebab-case | `data-company-id="123"` |
| Variables CSS (custom properties) | kebab-case con prefijo `--` | `--color-primary` |

---

## 4. jQuery

| Elemento | Convención | Ejemplo |
|---|---|---|
| Variables que almacenan objetos jQuery | Igual que C#: camelCase con prefijo `$` | `const $companyTable = $('#company-table');` |
| Constantes que almacenan objetos jQuery | Igual que C#: UPPER_SNAKE_CASE con prefijo `$` | `const $MAIN_TABLE = $('#main-table');` |
| Selectores | Usar `data-*` o clases, evitar IDs genéricos | `$('[data-action="delete"]')` |
| Nombres de eventos custom | kebab-case o namespaced | `$(el).trigger('company:updated')` |

Se aplica también la regla de `ID` en mayúsculas: `$companyID`, `$CompanyIDInput`.

---

## 5. PostgreSQL — Nomenclatura de tablas

| Elemento | Convención | Ejemplo |
|---|---|---|
| Tablas | `{NombreDelModulo}_NombreDeLaTabla` (PascalCase en cada parte) | `Items_AttributeOptions`, `Billing_Invoices` |

Toda tabla debe llevar como prefijo el nombre del módulo al que pertenece, seguido de guion bajo y el nombre de la tabla en PascalCase.

---

## 6. Regla transversal: la palabra "ID"

En **todas las tecnologías** (C#, TypeScript, jQuery), la palabra `ID` se escribe siempre completamente en mayúsculas, sin importar si es parte de una variable, parámetro, propiedad o nombre de método.

```csharp
var companyID = 1;

public Guid CompanyID { get; set; }

Task<Company> GetCompanyByID(Guid CompanyID)
{
    ...
}
```

```typescript
let companyID = 1;

function getCompanyByID(companyID: string) { ... }
```

---

## 7. Llamado de métodos: parámetros nombrados y en líneas separadas

Al invocar un método, se deben especificar los nombres de los parámetros, cada uno en su propia línea, respetando la identación.

### Umbral de aplicación

| Tipo de llamada | Se aplica desde |
|---|---|
| Métodos de un `*Repository` | **2 o más parámetros** |
| Cualquier otra llamada (helpers, services, `HasPermissionAsync`, etc.) | **3 o más parámetros** |

Por debajo del umbral, la llamada puede escribirse en una sola línea.

Correcto — método de Repository con 2 parámetros:

```csharp
await Repository.GetCompanyByID(
    CompanyID: CompanyID,
    UserID: UserID);
```

Incorrecto — mismo caso en una sola línea:

```csharp
await Repository.GetCompanyByID(CompanyID, UserID);
```

Correcto — Repository con 1 solo parámetro:

```csharp
await Repository.GetCompanyByID(CompanyID: CompanyID);
```

Correcto — llamada que no es de Repository, con 2 parámetros:

```csharp
await Permissions.HasPermissionAsync(UserID, ModuleID);
```

Correcto — llamada que no es de Repository, con 3 parámetros:

```csharp
await Permissions.HasPermissionAsync(
    UserID: UserID,
    ModuleID: ModuleID,
    Action: Action);
```

---

## 8. Notas generales

- Evitar abreviaturas ambiguas (`cmp` en vez de `company`), salvo estándares muy conocidos (`ID`, `Url`, `Db`).
- Mantener consistencia en singular/plural: entidades en singular (`Company`), colecciones/tablas en plural cuando aplique.
- Los nombres deben ser autoexplicativos; evitar nombres genéricos como `data`, `info`, `temp`, `obj`.
