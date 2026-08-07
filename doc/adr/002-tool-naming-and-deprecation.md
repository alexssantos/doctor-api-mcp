# ADR-002 — Nomes físicos e depreciação das tools

- Status: aceito
- Data: 2026-08-07

## Decisão

As tools vNext usam `snake_case` com escopo explícito (`service_get_health`, `system_get_health_summary`). Os nomes conceituais com ponto permanecem apenas na documentação. Isso preserva compatibilidade com clientes MCP que restringem identificadores.

Tools legadas mantêm nome e resposta durante pelo menos uma release, recebem aviso de depreciação na descrição e delegam a providers normalizados quando a semântica não muda. `get_health` continua significando pod health. `query_metrics` deixa de ser registrada por padrão e só pode existir em uma superfície administrativa separada, autenticada e habilitada explicitamente.

Não serão publicados aliases simultâneos com ponto e underscore.
