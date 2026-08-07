# ADR-004 — Fontes de sinais, histórico e baselines

- Status: aceito
- Data: 2026-08-07

## Decisão

- Métricas RED e de processo vêm do Prometheus por templates internos configuráveis; nenhuma PromQL externa alcança os engines.
- Estado de recursos vem do Kubernetes provider. Nodes ficam fora do escopo inicial.
- Baselines são consultados no Prometheus (janela anterior, 24h e 7d). Não há store materializado até métricas de custo/latência justificarem.
- Deploys usam Kubernetes Events, ReplicaSets, revisão e troca de imagem no MVP. O provider preserva snapshots em memória; o overlay AKS deve plugar uma fonte GitOps/CI durável.
- Logs vêm do Loki por operações tipadas, com limite e redaction anteriores ao cache.

Ausência de qualquer fonte é representada como `unavailable` ou `stale`, nunca como zero.
