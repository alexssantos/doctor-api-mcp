# `explain_api`

## Objetivo

Explica uma API combinando duas fontes: o contrato OpenAPI, que descreve o que a API oferece, e os traces recentes do Jaeger, que mostram como ela foi usada.

Para uma crianca de 10 anos: e como juntar o livro de regras de um jogo com uma lista das partidas que acabaram de acontecer.

## Contrato MCP

- **Nome:** `explain_api`
- **Parametro obrigatorio:** `serviceName` (`string`), por exemplo `precoapi` ou `produtoapi`.
- **Retorno:** texto JSON formatado.

## Como funciona

1. Bloqueia a consulta se a aplicacao estiver desabilitada no catalogo.
2. Carrega as rotas OpenAPI pelo `IOpenApiCollector`.
3. Resolve o nome do servico no Jaeger, priorizando `otelServiceName` do catalogo. Sem esse valor, procura uma correspondencia sem diferenciar maiusculas e minusculas na lista do Jaeger.
4. Busca ate dez spans recentes e agrupa por operacao.
5. Calcula `callCount` e `avgDurationMs` para cada operacao observada.

Falhas ao consultar os traces nao impedem a explicacao da API: `recentActivity` pode ser `null` quando Jaeger nao tem dados ou esta indisponivel.

## Estrutura de resposta

```json
{
  "service": "produtoapi",
  "routes": [
    {
      "method": "GET",
      "path": "/api/products",
      "summary": "Lista produtos",
      "operationId": "GetProducts"
    }
  ],
  "recentActivity": [
    {
      "operation": "GET /api/products",
      "callCount": 10,
      "avgDurationMs": 18.4
    }
  ]
}
```

## Exemplo de uso

```text
Use explain_api com serviceName="precoapi" para explicar suas rotas e indicar quais operacoes tiveram atividade recente.
```

## Limitacoes

- As rotas dependem de uma especificacao OpenAPI acessivel.
- As estatisticas refletem apenas os ate dez spans recuperados, nao uma janela analitica completa.
- A tool nao retorna todos os detalhes de cada trace; para isso, use `trace_route`.
