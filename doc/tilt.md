# Desenvolvimento local com Tilt

O [Tilt](https://tilt.dev) automatiza o ciclo de desenvolvimento no cluster Kubernetes local. Em vez de executar manualmente `docker build`, `kubectl apply` e port-forwards a cada mudança de código, o Tilt observa os arquivos e reaplica apenas o que mudou — rebuild de imagem, redeploy do serviço e portforwarding tudo de uma vez.

---

## Pré-requisitos

| Ferramenta | Versão mínima | Verificar |
|---|---|---|
| [Tilt](https://docs.tilt.dev/install.html) | 0.33+ | `tilt version` |
| Docker Desktop | qualquer | `docker version` |
| k3d | 5+ | `k3d version` |
| kubectl | qualquer | `kubectl version --client` |

O cluster k3d deve estar criado e como contexto ativo do kubectl antes de rodar o Tilt. Consulte os scripts em `infra/scripts/` para a criação do cluster.

---

## Subindo o ambiente

```bash
# na raiz do repositório
tilt up
```

O comando abre a UI web em `http://localhost:10350` e inicia o pipeline:

1. Aplica o namespace `mcp-apis`
2. Sobe a infraestrutura (Postgres, Jaeger, Prometheus, Loki, Promtail, Grafana)
3. Faz o build das imagens das três aplicações (.NET 10)
4. Aplica os manifestos Kubernetes de cada serviço
5. Cria os port-forwards automaticamente

Aguarde todos os recursos ficarem verdes na UI antes de usar o ambiente.

---

## Port-forwards provisionados automaticamente

| Serviço | URL local | Descrição |
|---|---|---|
| **PrecoAPI** | `http://localhost:8081` | REST API de preços |
| **ProdutoAPI** | `http://localhost:8082` | REST API de produtos |
| **McpServer** | `http://localhost:4000` | Servidor MCP (POST /) |
| **Grafana** | `http://localhost:3000` | Dashboards (admin/admin) |
| **Prometheus** | `http://localhost:9090` | Métricas |
| **Jaeger UI** | `http://localhost:16686` | Distributed tracing |
| **Postgres preco** | `localhost:5433` | Banco `preco_db` |
| **Postgres produto** | `localhost:5434` | Banco `produto_db` |

---

## Ordem de dependências

O Tiltfile declara as dependências entre recursos para garantir que a infraestrutura esteja pronta antes das aplicações:

```
postgres-preco ─┐
                ├─► precoapi ─┐
jaeger ─────────┘             ├─► mcpserver
                              │
postgres-produto ─┐           │
                  ├─► produtoapi ─┘
jaeger ───────────┘
```

O Tilt respeita essa ordem na inicialização e durante rebuilds parciais.

---

## Hot reload — como o Tilt detecta mudanças

O Tilt monitora os diretórios listados no `only` de cada `docker_build`. Ao salvar qualquer arquivo dentro desses diretórios, o rebuild é disparado apenas para os serviços afetados:

| Você alterou… | Serviço(s) rebuiltados |
|---|---|
| `src/Services/PrecoAPI/` | `precoapi` |
| `src/Services/ProdutoAPI/` | `produtoapi` |
| `src/Services/McpServer/` | `mcpserver` |
| `src/BuildingBlocks/` | todos os três (é dependência compartilhada) |
| `infra/k8s/precoapi/*.yaml` | apenas os manifestos do `precoapi` (sem rebuild de imagem) |

Alterações em arquivos YAML não disparam rebuild de imagem — o Tilt faz apenas `kubectl apply` no manifesto modificado.

---

## Encerrando o ambiente

```bash
# Ctrl+C no terminal onde o tilt up está rodando, ou:
tilt down
```

`tilt down` remove todos os recursos do cluster que o Tilt gerencia, mas **não destrói o cluster k3d** nem os volumes persistentes dos bancos.

---

## Estrutura do Tiltfile

```
Tiltfile
│
├── namespace.yaml
│
├── Infraestrutura (k8s_yaml — sem build)
│   ├── postgres-preco/   (secret, configmap-init, service, statefulset)
│   ├── postgres-produto/ (secret, configmap-init, service, statefulset)
│   ├── jaeger/           (deployment, service)
│   ├── prometheus/       (configmap, deployment, service, ingress)
│   ├── loki/             (configmap, deployment, service)
│   ├── promtail/         (configmap, daemonset)
│   └── grafana/          (configmap-datasources, deployment, service, ingress)
│
└── Aplicações (docker_build + k8s_yaml)
    ├── precoapi   → src/Services/PrecoAPI/Dockerfile
    ├── produtoapi → src/Services/ProdutoAPI/Dockerfile
    └── mcpserver  → src/Services/McpServer/Dockerfile
```

> **Contexto de build:** todos os `docker_build` usam `.` (raiz do repositório) como contexto porque os Dockerfiles copiam arquivos de `src/BuildingBlocks/` que ficam fora do diretório do serviço. O campo `only` restringe quais diretórios são enviados ao daemon Docker, evitando invalidar o cache desnecessariamente.

---

## Solução de problemas comuns

### Recurso travado em "Pending" ou "Error"

1. Clique no recurso na UI do Tilt para ver os logs detalhados.
2. Para forçar um rebuild/reapply manual:
   ```bash
   # pela UI: botão "Trigger" ao lado do recurso
   # ou pelo CLI:
   tilt trigger precoapi
   ```

### Imagem não atualizada no cluster

O `imagePullPolicy: Never` nos deployments garante que o k3d use sempre a imagem local buildada pelo Tilt. Se o pod estiver usando uma imagem antiga, force um redeploy:

```bash
tilt trigger precoapi
```

### Port-forward cai

O Tilt monitora e reinicia port-forwards automaticamente. Se persistir, reinicie o Tilt (`Ctrl+C` + `tilt up`).

### Banco de dados não inicializa

O script de init SQL fica em `infra/k8s/postgres-preco/configmap-init.yaml` e `infra/k8s/postgres-produto/configmap-init.yaml`. O script só roda na **primeira inicialização do volume**. Se precisar reinicializar:

```bash
# delete o PVC e o StatefulSet para recriar do zero
kubectl delete statefulset postgres-preco -n mcp-apis
kubectl delete pvc -l app=postgres-preco -n mcp-apis
tilt trigger postgres-preco
```

---

## Dicas de uso

- **Logs em tempo real:** clique em qualquer recurso na UI do Tilt para ver o stdout/stderr do container atualizado em tempo real.
- **Somente infraestrutura:** não é possível subir parcialmente com o `tilt up`, mas você pode usar o botão **Disable** na UI para pausar o rebuild de serviços específicos enquanto trabalha em outro.
- **Múltiplos terminais:** o `tilt up` pode ficar rodando em background — use a UI web para monitorar. O terminal só é necessário para o `tilt down`.
