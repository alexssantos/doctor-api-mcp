# `find_data_origin`

## Objetivo

Investiga de onde vem o dado retornado por uma rota. Combina o endpoint declarado no OpenAPI, traces Jaeger e informacoes de pods Kubernetes para apresentar o caminho observado ate integracoes HTTP ou banco de dados.

Para uma crianca de 10 anos: e como descobrir de onde veio a informacao de uma pesquisa, seguindo a pessoa que perguntou, quem respondeu e o livro que foi consultado.

## Contrato MCP

- **Nome:** `find_data_origin`
- **Parametros obrigatorios:**
  - `serviceName` (`string`), por exemplo `produtoapi`.
  - `route` (`string`), por exemplo `/api/produtos/{id}`.
- **Retorno:** texto JSON formatado.

## Como funciona

1. Bloqueia a consulta se a aplicacao estiver desabilitada para indexacao.
2. Carrega as rotas OpenAPI e procura uma cujo `Path` seja igual a `route`, sem diferenciar maiusculas e minusculas.
3. Resolve o nome OpenTelemetry do servico e busca ate dez spans recentes no Jaeger.
4. Seleciona spans cujo `http.route` seja a rota solicitada ou cuja operacao contenha a rota. Sem correspondencia, usa todos os spans recuperados como fallback.
5. Considera no maximo tres `traceId` e monta uma cadeia por trace.
6. Em cada span inclui dados de HTTP e, quando instrumentados, `db.statement` e `db.system`.
7. Lista pods cujo campo `app` corresponde ao nome de servico solicitado.

## Estrutura de resposta

```json
{
  "service": "produtoapi",
  "route": "/api/products/{id}",
  "matchingEndpoint": {
    "method": "GET",
    "path": "/api/products/{id}",
    "summary": "Busca produto"
  },
  "dataFlow": [
    {
      "traceId": "abc123",
      "chain": [
        {
          "serviceName": "ProdutoAPI",
          "operationName": "GET /api/products/{id}",
          "durationMs": 12.5,
          "dbStatement": "",
          "dbSystem": "postgresql",
          "httpRoute": "/api/products/{id}",
          "httpMethod": "GET",
          "peerService": "PrecoAPI"
        }
      ]
    }
  ],
  "runningPods": [{ "name": "produtoapi-abc", "status": "Running", "ready": true }]
}
```

## Exemplo de uso

```text
Use find_data_origin com serviceName="produtoapi" e route="/api/products/{id}" para mostrar se a resposta consulta PostgreSQL e PrecoAPI.
```

## Limitacoes

- O fallback para todos os spans pode misturar atividade de outras rotas quando nao ha trace especifico.
- A cadeia e ordenada por duracao do span, nao pela relacao pai-filho nem pelo instante de inicio.
- `db.statement` pode estar vazio por configuracao de privacidade ou falta de instrumentacao.
