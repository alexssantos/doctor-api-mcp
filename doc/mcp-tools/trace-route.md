# `trace_route`

## Objetivo

Busca traces recentes no Jaeger para um servico e, opcionalmente, para uma operacao ou rota. A resposta agrupa spans por trace e mostra a cadeia observada de chamadas, duracoes e dados HTTP.

Para uma crianca de 10 anos: e como seguir as pegadas de uma encomenda, desde quando ela entrou na loja ate as pessoas que ajudaram a leva-la adiante.

## Contrato MCP

- **Nome:** `trace_route`
- **Parametros:**
  - `service` (`string`, obrigatorio): nome do servico, por exemplo `PrecoAPI` ou `ProdutoAPI`.
  - `operation` (`string`, opcional): nome da operacao ou rota para filtrar, por exemplo `GET /api/precos`.
  - `limit` (`integer`, opcional, padrao `5`): quantidade maxima de traces.
- **Retorno:** texto JSON formatado.

## Como funciona

1. Verifica no catalogo se o servico esta habilitado para indexacao. Se estiver desabilitado, retorna um JSON de erro com instrucao para habilita-lo no dashboard.
2. Resolve aliases para o nome OpenTelemetry armazenado no catalogo, pois Jaeger diferencia maiusculas e minusculas.
3. Busca spans no Jaeger, usando o filtro e limite fornecidos.
4. Agrupa spans por `traceId`.
5. Para cada span, retorna operacao, servico, duracao em milissegundos e tags HTTP quando existirem.

## Estrutura de resposta

```json
[
  {
    "traceId": "abc123",
    "spans": [
      {
        "operationName": "GET /api/products",
        "serviceName": "ProdutoAPI",
        "durationMs": 24.5,
        "httpMethod": "GET",
        "httpRoute": "/api/products",
        "httpStatus": "200"
      }
    ]
  }
]
```

## Exemplo de uso

```text
Use trace_route com service="ProdutoAPI", operation="GET /api/products" e limit=3 para investigar chamadas lentas.
```

## Limitacoes

- Sem trafego recente, o Jaeger pode nao ter spans para retornar.
- A ordem dos spans e a recebida do coletor; ela nao e reorganizada em uma arvore pai-filho.
- O bloqueio por indexacao e aplicado antes da consulta ao Jaeger.
