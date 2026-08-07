# `query_metrics`

## Objetivo

Executa uma consulta PromQL no Prometheus e devolve metricas atuais, como disponibilidade de targets, taxa de requisicoes, erros, latencia, CPU ou memoria.

Para uma crianca de 10 anos: e como fazer uma pergunta a um placar que guarda varios contadores, por exemplo: "quantas vezes a campainha tocou nos ultimos cinco minutos?".

## Contrato MCP

- **Nome:** `query_metrics`
- **Parametro obrigatorio:** `query` (`string`), uma expressao PromQL.
- **Retorno:** texto JSON serializado com a resposta do Prometheus.

## Como funciona

1. Examina o texto da consulta procurando aliases de aplicacoes desabilitadas: nome da aplicacao, Service Kubernetes, Deployment ou nome OpenTelemetry.
2. Se encontrar uma aplicacao desabilitada, retorna um JSON de erro e nao consulta Prometheus.
3. Caso contrario, chama `IPrometheusCollector.QueryAsync(query)` e serializa o resultado.

A verificacao e de melhor esforco: PromQL aceita expressoes livres, matchers regex e transformacoes que podem impedir uma identificacao completa pelo texto.

## Exemplos de uso

Disponibilidade dos targets:

```text
Use query_metrics com query="up" para verificar quais targets estao acessiveis ao Prometheus.
```

Taxa de requisicoes da PrecoAPI:

```text
Use query_metrics com query="sum(rate(http_server_request_duration_seconds_count{service=\"precoapi\"}[5m]))".
```

Exemplo ilustrativo de retorno do Prometheus:

```json
{
  "status": "success",
  "data": {
    "resultType": "vector",
    "result": []
  }
}
```

## Limitacoes

- Executa apenas consulta instantanea; a janela, quando necessaria, deve estar na propria PromQL, como `[5m]`.
- A tool e um passthrough de PromQL: a validade, custo e semantica da consulta dependem do Prometheus.
- O bloqueio de aplicacoes desabilitadas nao e hermetico e nao deve ser tratado como controle de seguranca para PromQL arbitraria.
