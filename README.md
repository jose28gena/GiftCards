# GiftCards API

[![CI](https://github.com/jose28gena/GiftCards/actions/workflows/ci.yml/badge.svg)](https://github.com/jose28gena/GiftCards/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

Servicio transaccional de **tarjetas de regalo** pensado para integrarse con terminales de punto de venta (POS). El foco está en lo que importa en sistemas de pagos: **no cobrar dos veces, no sobregirar una tarjeta aunque lleguen cobros simultáneos y poder auditar cada movimiento**.

## Problemas que resuelve

| Problema real en un POS | Cómo se resuelve aquí |
| --- | --- |
| La red falla y la terminal reintenta el cobro | **Idempotencia**: cada operación exige un encabezado `Idempotency-Key`. Repetirla devuelve el resultado original sin volver a cobrar. |
| Dos terminales cobran la misma tarjeta a la vez | **Concurrencia optimista** con token de versión y reintento controlado. Nunca se sobregira. |
| Dos peticiones idénticas llegan en paralelo | Además del chequeo en código, un **índice único** `(tarjeta, idempotency-key)` en la base de datos cierra la carrera. |
| Errores de redondeo con dinero | Los montos se guardan en **centavos** (`long`), no en `decimal` ni `double`. |
| Auditoría y conciliación | **Libro de movimientos inmutable**: el saldo siempre puede reconstruirse sumando movimientos. |
| Un cobro que excede el saldo o un reembolso mayor a lo cobrado | Reglas de negocio dentro de la entidad de dominio, con errores `422` explícitos. |

## Endpoints

Todas las rutas `/api/*` requieren el encabezado `X-Api-Key`. Las operaciones que modifican saldo requieren además `Idempotency-Key`.

| Método | Ruta | Descripción |
| --- | --- | --- |
| POST | `/api/giftcards` | Emite una tarjeta nueva, sin saldo y pendiente de activación |
| GET | `/api/giftcards/{code}` | Consulta saldo y estado |
| POST | `/api/giftcards/{code}/activate` | Activa con un saldo inicial |
| POST | `/api/giftcards/{code}/redeem` | Cobra un monto |
| POST | `/api/giftcards/{code}/refund` | Reembolsa, sin exceder lo cobrado |
| GET | `/api/giftcards/{code}/transactions` | Libro de movimientos |
| GET | `/health` | Estado del servicio (sin autenticación) |

Códigos de respuesta de negocio:

| Código | Cuándo |
| --- | --- |
| `400` | Monto inválido (cero, negativo, más de 2 decimales, mayor a 1,000,000) o falta `Idempotency-Key` |
| `401` | Falta o es inválida la `X-Api-Key` |
| `404` | Tarjeta inexistente |
| `409` | Estado incompatible (tarjeta no activa, ya activada) |
| `422` | Saldo insuficiente, reembolso excesivo o `Idempotency-Key` reutilizada con otra petición |

### Ejemplo

```bash
KEY="tu-api-key"

# 1. Emitir
curl -s -X POST http://localhost:8080/api/giftcards -H "X-Api-Key: $KEY"
# {"code":"GC-7K3M-QX9P-2HFA","balance":0,"currency":"MXN","status":"Issued", ...}

# 2. Activar con 500.00
curl -s -X POST http://localhost:8080/api/giftcards/GC-7K3M-QX9P-2HFA/activate \
  -H "X-Api-Key: $KEY" -H "Idempotency-Key: act-001" -H "Content-Type: application/json" \
  -d '{"amount": 500.00}'

# 3. Cobrar 125.50 (si la terminal reintenta con la misma Idempotency-Key, no se vuelve a cobrar)
curl -s -X POST http://localhost:8080/api/giftcards/GC-7K3M-QX9P-2HFA/redeem \
  -H "X-Api-Key: $KEY" -H "Idempotency-Key: ticket-4471" -H "Content-Type: application/json" \
  -d '{"amount": 125.50, "reference": "ticket-4471"}'
```

Una respuesta repetida incluye `"replayed": true` y el encabezado `Idempotent-Replayed: true`.

## Arquitectura

```
src/
├── GiftCards.Domain          # GiftCard (reglas de negocio) y LedgerEntry. Sin dependencias.
├── GiftCards.Application     # GiftCardService: idempotencia, reintentos de concurrencia, DTOs
├── GiftCards.Infrastructure  # EF Core + SQLite (índices únicos, token de concurrencia)
└── GiftCards.Api             # Controllers, middleware de API key y de errores
tests/
└── GiftCards.Tests           # 26 tests: dominio (unitarios) + API completa (integración)
```

Las reglas viven en la entidad `GiftCard` (`Activate`, `Redeem`, `Refund`); el servicio solo orquesta persistencia, idempotencia y reintentos.

## Ejecutar en local

Requisitos: [.NET 8 SDK](https://dotnet.microsoft.com/download).

La API key **no** está en el repositorio. Defínela como variable de entorno (mínimo 16 caracteres); la API no arranca sin ella:

```powershell
# PowerShell
$env:Security__ApiKey = "una-clave-de-al-menos-16-caracteres"
dotnet run --project src/GiftCards.Api
```

```bash
# Bash
export Security__ApiKey="una-clave-de-al-menos-16-caracteres"
dotnet run --project src/GiftCards.Api
```

Swagger queda en `/swagger`. Se crea una base SQLite `giftcards.db` en el directorio de trabajo.

## Con Docker

```bash
export API_KEY="$(openssl rand -hex 24)"
docker compose up --build
# API en http://localhost:8080/swagger
```

## Tests

```bash
dotnet test
```

Incluyen casos que ejercitan los puntos delicados con la API real:

- 30 cobros simultáneos de 10.00 sobre una tarjeta de 100.00: **exactamente 10 proceden, 20 reciben `422` y el saldo final es 0.00**.
- 10 peticiones paralelas con la misma `Idempotency-Key`: el cobro se aplica una sola vez.
- Reutilizar una clave con otro monto: `422` y el saldo no cambia.
- El saldo se reconstruye sumando el libro de movimientos.

## Decisiones de diseño y límites

- **SQLite por simplicidad**: sin infraestructura externa. En producción usaría SQL Server o PostgreSQL; el token de concurrencia y los índices únicos se mantienen igual. SQLite serializa escrituras (se activa WAL), así que el throughput no es representativo.
- **`EnsureCreated` en lugar de migraciones**: suficiente para una demo; en producción, migraciones de EF Core.
- **Autenticación por API key**: modela una terminal de confianza. Para varios comercios usaría una clave por terminal y rotación.
- **Reintentos de concurrencia acotados** (50 intentos con espera aleatoria corta); si se agotan, responde `409`.

## Licencia

MIT. Consulta [LICENSE](LICENSE).
