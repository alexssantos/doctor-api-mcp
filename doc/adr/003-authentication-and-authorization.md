# ADR-003 — Autenticação e autorização

- Status: aceito para o incremento local; substituível por identidade gerenciada no AKS
- Data: 2026-08-07

## Decisão

O servidor oferece um esquema de API key no header `X-Observability-Api-Key` (ou Bearer token) com dois perfis:

- `ObservabilityReader`: tools vNext, consultas do dashboard e endpoints read-only;
- `ObservabilityAdmin`: inventário completo, rescan e toggle de indexação.

Quando `Security:Authentication:Enabled=false`, apenas o ambiente local explicitamente configurado recebe uma identidade de desenvolvimento com ambos os perfis. Fora de Development, startup falha se autenticação estiver desabilitada ou se as chaves não estiverem configuradas.

O overlay AKS deverá substituir API key por OIDC/Entra Workload Identity no ponto de entrada sem alterar as policies internas.

Endpoints `/live` e `/ready` não expõem dados e permanecem anônimos para probes. `/metrics` e o status detalhado exigem policy de leitura.
