# ADR-001 — Limite read-only e estado administrativo

- Status: aceito
- Data: 2026-08-07

## Contexto

O MCP observa workloads e precisa permanecer incapaz de alterar aplicações, mas o toggle de indexação já persiste estado próprio no ConfigMap `mcpserver-state`.

## Decisão

O plano de dados é estritamente read-only. O service account não recebe `create`, `delete`, `update` ou `patch` sobre workloads, Secrets ou ConfigMaps das aplicações.

Mantemos uma exceção de control plane limitada ao ConfigMap `mcpserver-state`, no namespace do MCP e restrita por `resourceNames`. Apenas endpoints administrativos podem solicitar essa gravação; engines e tools analíticas não recebem o store por injeção. Toda alteração gera audit log.

Se a governança do ambiente exigir read-only literal, `IndexingState:Provider=Memory` desativa a persistência Kubernetes sem mudar os contratos analíticos.

## Consequências

- Recomendações continuam sempre com `executable=false`.
- O RBAC pode ser validado por uma matriz positiva de leitura e negativa de mutação.
- A exceção é removível sem afetar providers ou engines.
