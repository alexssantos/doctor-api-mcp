# 📋 Casos de Uso — AI Agent + MCP para Observabilidade de Microserviços

---

## Contexto

O agente tem acesso a três fontes de dados via MCP:

| Fonte        | O que fornece                              |
| ------------ | ------------------------------------------ |
| Scalar / OpenAPI | Contratos, rotas, modelos de dados     |
| Jaeger       | Traces reais de execução entre serviços    |
| Kubernetes API | Estado dos pods, deployments, services  |

---

## UC-01 — Entender o que uma API faz

**Pergunta:** _"O que a ProductAPI faz?"_

**Fluxo:**
1. Agent chama `get_openapi(service="productapi")`
2. MCP busca `GET http://productapi/openapi/v1.json`
3. Agent analisa paths, operações e schemas

**Resposta esperada:**
```
ProductAPI gerencia o catálogo de produtos. Expõe:
  - GET  /api/products        → lista todos os produtos com preço atual
  - GET  /api/products/{id}   → detalhe de um produto
  - POST /api/products        → cria produto
  - PUT  /api/products/{id}   → atualiza produto
  - DELETE /api/products/{id} → remove produto

Integra com PriceAPI para enriquecer cada produto com preço e moeda.
```

---

## UC-02 — Descobrir quem chama uma rota

**Pergunta:** _"Quem chama a PriceAPI?"_

**Fluxo:**
1. Agent chama `find_dependencies(service="priceapi")`
2. MCP consulta `GET http://jaeger:16686/api/traces?service=priceapi`
3. Analisa spans para identificar serviços de origem

**Resposta esperada:**
```
A PriceAPI é chamada por:
  - ProductAPI → GET /api/prices/{productId}
    (para enriquecer produtos com preço no momento do GET /api/products)

Nenhum cliente externo direto encontrado nos últimos traces.
```

---

## UC-03 — Rastrear a origem de um dado

**Pergunta:** _"De onde vem o preço exibido no produto?"_

**Fluxo:**
1. Agent chama `trace_route(operation="GET /api/products/{id}")`
2. MCP busca traces da operação no Jaeger
3. Reconstrói o grafo de chamadas do trace

**Resposta esperada:**
```
Fluxo para GET /api/products/{id}:

  Client
    → ProductAPI: GET /api/products/{id}            (3ms)
        → PriceAPI: GET /api/prices/{productId}     (1ms)
            → postgres-preco (query SELECT)         (0.4ms)

O campo "price.value" vem da tabela `prices` no banco postgres-preco,
consultado pela PriceAPI a cada requisição (sem cache).
```

---

## UC-04 — Verificar saúde de uma rota

**Pergunta:** _"A rota GET /api/products está saudável?"_

**Fluxo:**
1. Agent chama `get_health(service="productapi", route="GET /api/products")`
2. MCP consulta Jaeger: taxa de erro e latência dos últimos traces
3. MCP consulta K8s API: status do pod

**Resposta esperada:**
```
GET /api/products — ProductAPI

  Pod status    : Running (2/2 réplicas)
  Últimos traces: 47 requisições na última hora
  Taxa de erro  : 0%
  Latência P50  : 12ms
  Latência P99  : 38ms

✅ Rota saudável.
```

**Variante com problema:**
```
GET /api/products — ProductAPI

  Pod status    : 1/2 Running  (1 pod em CrashLoopBackOff)
  Taxa de erro  : 23% (últimos 10 min)
  Erro recorrente: "Connection refused" ao chamar PriceAPI

⚠️ Degradada. PriceAPI pode estar inacessível.
```

---

## UC-05 — Explicar o fluxo completo de uma operação

**Pergunta:** _"Explique o fluxo completo de criação de um produto"_

**Fluxo:**
1. Agent chama `trace_route(operation="POST /api/products")`
2. Agent chama `get_openapi` para obter o schema do request/response

**Resposta esperada:**
```
Fluxo: POST /api/products

  1. Client envia:
     { "name": "...", "description": "...", "stock": 10 }

  2. ProductAPI valida o payload (schema: CreateProductRequest)

  3. ProductAPI persiste em postgres-produto
     → INSERT INTO products (id, name, description, stock)

  4. Retorna 201 Created com o produto criado (sem preço — preço é
     consultado apenas no GET, não no momento da criação)

Tempo médio observado nos traces: 8ms
```

---

## UC-06 — Listar todos os serviços e seu estado

**Pergunta:** _"Quais serviços estão rodando no cluster?"_

**Fluxo:**
1. Agent chama `list_services()`
2. MCP consulta K8s API: `/api/v1/namespaces/mcp-apis/pods` e `/services`
3. Cruza com serviços encontrados no Jaeger

**Resposta esperada:**
```
Serviços no namespace mcp-apis:

  Serviço          Pods       Status     Último trace
  ─────────────────────────────────────────────────────
  productapi       2/2 ✅     Running    há 2 min
  priceapi         2/2 ✅     Running    há 2 min
  jaeger           1/1 ✅     Running    —
  postgres-produto 1/1 ✅     Running    —
  postgres-preco   1/1 ✅     Running    —
```

---

## UC-07 — Identificar dependências entre serviços

**Pergunta:** _"Qual é o mapa de dependências das APIs?"_

**Fluxo:**
1. Agent chama `find_dependencies()` para todos os serviços
2. MCP agrega traces do Jaeger para montar o grafo

**Resposta esperada:**
```
Mapa de dependências:

  Client
    └─▶ ProductAPI
          ├─▶ PriceAPI
          │     └─▶ postgres-preco
          └─▶ postgres-produto

  PriceAPI
    └─▶ postgres-preco (somente leitura para enrichment)
```

---

## UC-08 — Diagnosticar um erro em produção

**Pergunta:** _"Por que o GET /api/products está retornando erro?"_

**Fluxo:**
1. Agent chama `get_health(service="productapi", route="GET /api/products")`
2. MCP identifica traces com erro → busca detalhes do span com falha
3. MCP consulta K8s API para ver eventos do pod

**Resposta esperada:**
```
Diagnóstico — GET /api/products

  Erro encontrado: HTTP 500 (38% das requisições)
  Span com falha : PriceAPI → GET /api/prices/{productId}
  Mensagem       : "dial tcp: connection refused"

  Estado do pod priceapi:
    → 0/2 Running  (ambos em CrashLoopBackOff)
    → Último evento K8s: "OOMKilled" (limite de memória excedido)

Causa raiz provável: PriceAPI está sendo encerrada por OOM.
Ação sugerida   : aumentar o limits.memory no deployment da PriceAPI
                  ou investigar vazamento de memória.
```

---

## UC-09 — Comparar comportamento antes e depois de um deploy

**Pergunta:** _"A latência do GET /api/products piorou após o último deploy?"_

**Fluxo:**
1. Agent chama `trace_route` filtrando por janela de tempo pré e pós-deploy
2. Compara P50/P99 entre os dois períodos

**Resposta esperada:**
```
Comparação de latência — GET /api/products

              Antes do deploy   Após o deploy
  ─────────────────────────────────────────────
  P50            12ms              28ms   ⚠️
  P99            38ms             140ms   ⚠️
  Taxa de erro    0%                2%    ⚠️

A latência aumentou ~2.3x após o deploy das 14h32.
O span mais lento é a chamada ao postgres-produto (de 5ms → 20ms).
Possível causa: nova query sem índice ou aumento de volume de dados.
```

---

## UC-10 — Responder sobre o contrato de integração entre serviços

**Pergunta:** _"Qual é o contrato de dados entre ProductAPI e PriceAPI?"_

**Fluxo:**
1. Agent chama `get_openapi` para ProductAPI e PriceAPI
2. Cruza o schema do request enviado com o response esperado

**Resposta esperada:**
```
Integração ProductAPI → PriceAPI

  ProductAPI envia:
    GET /api/prices/{productId}
    productId: Guid

  PriceAPI retorna (200 OK):
    {
      "productId": "guid",
      "value": 49.90,
      "currency": "BRL"
    }

  Em caso de preço não encontrado: 404 (ProductAPI trata retornando
  price: null no produto, sem quebrar a resposta).
```

---

## Matriz de Fases × Casos de Uso

| Caso de Uso | Fase 1 (Scalar) | Fase 2 (Jaeger) | Fase 3 (Métricas) | Fase 4 (MCP) | Fase 5 (Agent) |
| ----------- | :---: | :---: | :---: | :---: | :---: |
| UC-01 Entender a API            | ✅ | — | — | ✅ | ✅ |
| UC-02 Quem chama uma rota       | — | ✅ | — | ✅ | ✅ |
| UC-03 Origem de um dado         | ✅ | ✅ | — | ✅ | ✅ |
| UC-04 Saúde de uma rota         | — | ✅ | ✅ | ✅ | ✅ |
| UC-05 Fluxo completo            | ✅ | ✅ | — | ✅ | ✅ |
| UC-06 Listar serviços           | — | ✅ | — | ✅ | ✅ |
| UC-07 Mapa de dependências      | — | ✅ | — | ✅ | ✅ |
| UC-08 Diagnosticar erro         | — | ✅ | — | ✅ | ✅ |
| UC-09 Comparar latência         | — | ✅ | ✅ | ✅ | ✅ |
| UC-10 Contrato de integração    | ✅ | — | — | ✅ | ✅ |
