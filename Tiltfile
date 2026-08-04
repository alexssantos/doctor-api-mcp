# =============================================================================
# Tiltfile — mcp-apis
# =============================================================================

# Namespace
k8s_yaml('infra/k8s/namespace.yaml')

# =============================================================================
# Infrastructure
# =============================================================================

k8s_yaml([
    # Postgres - Preco
    'infra/k8s/banco/postgres-preco/secret.yaml',
    'infra/k8s/banco/postgres-preco/configmap-init.yaml',
    'infra/k8s/banco/postgres-preco/service.yaml',
    'infra/k8s/banco/postgres-preco/statefulset.yaml',
    # Postgres - Produto
    'infra/k8s/banco/postgres-produto/secret.yaml',
    'infra/k8s/banco/postgres-produto/configmap-init.yaml',
    'infra/k8s/banco/postgres-produto/service.yaml',
    'infra/k8s/banco/postgres-produto/statefulset.yaml',
    # Jaeger
    'infra/k8s/observabilidade/jaeger/deployment.yaml',
    'infra/k8s/observabilidade/jaeger/service.yaml',
    # Prometheus
    'infra/k8s/observabilidade/prometheus/configmap.yaml',
    'infra/k8s/observabilidade/prometheus/deployment.yaml',
    'infra/k8s/observabilidade/prometheus/service.yaml',
    'infra/k8s/observabilidade/prometheus/ingress.yaml',
    # Loki
    'infra/k8s/observabilidade/loki/configmap.yaml',
    'infra/k8s/observabilidade/loki/deployment.yaml',
    'infra/k8s/observabilidade/loki/service.yaml',
    # Promtail
    'infra/k8s/observabilidade/promtail/configmap.yaml',
    'infra/k8s/observabilidade/promtail/daemonset.yaml',
    # Grafana
    'infra/k8s/observabilidade/grafana/secret.yaml',
    'infra/k8s/observabilidade/grafana/configmap-datasources.yaml',
    'infra/k8s/observabilidade/grafana/deployment.yaml',
    'infra/k8s/observabilidade/grafana/service.yaml',
    'infra/k8s/observabilidade/grafana/ingress.yaml',
])

k8s_resource('postgres-preco',  port_forwards=['5433:5432'])
k8s_resource('postgres-produto', port_forwards=['5434:5432'])
k8s_resource('jaeger',     port_forwards=['16686:16686'])
k8s_resource('prometheus', port_forwards=['9090:9090'])
k8s_resource('grafana',    port_forwards=['3000:3000'])

# =============================================================================
# Application Services
# =============================================================================

# Build contexts are at repo root because Dockerfiles reference src/ paths
docker_build(
    'precoapi',
    context='.',
    dockerfile='src/Services/PrecoAPI/Dockerfile',
    only=[
        'src/mcp-apis.slnx',
        'src/BuildingBlocks/',
        'src/Services/PrecoAPI/',
        'src/Services/ProdutoAPI/',
        'src/Services/McpServer/',
    ],
)

docker_build(
    'produtoapi',
    context='.',
    dockerfile='src/Services/ProdutoAPI/Dockerfile',
    only=[
        'src/mcp-apis.slnx',
        'src/BuildingBlocks/',
        'src/Services/PrecoAPI/',
        'src/Services/ProdutoAPI/',
        'src/Services/McpServer/',
    ],
)

docker_build(
    'mcpserver',
    context='.',
    dockerfile='src/Services/McpServer/Dockerfile',
    only=[
        'src/mcp-apis.slnx',
        'src/BuildingBlocks/',
        'src/Services/PrecoAPI/',
        'src/Services/ProdutoAPI/',
        'src/Services/McpServer/',
    ],
)

k8s_yaml([
    # PrecoAPI
    'infra/k8s/aplicacao/precoapi/secret.yaml',
    'infra/k8s/aplicacao/precoapi/configmap.yaml',
    'infra/k8s/aplicacao/precoapi/deployment.yaml',
    'infra/k8s/aplicacao/precoapi/service.yaml',
    'infra/k8s/aplicacao/precoapi/ingress.yaml',
    # ProdutoAPI
    'infra/k8s/aplicacao/produtoapi/secret.yaml',
    'infra/k8s/aplicacao/produtoapi/configmap.yaml',
    'infra/k8s/aplicacao/produtoapi/deployment.yaml',
    'infra/k8s/aplicacao/produtoapi/service.yaml',
    'infra/k8s/aplicacao/produtoapi/ingress.yaml',
    # McpServer
    'infra/k8s/aplicacao/mcpserver/rbac.yaml',
    'infra/k8s/aplicacao/mcpserver/configmap.yaml',
    'infra/k8s/aplicacao/mcpserver/state-configmap.yaml',
    'infra/k8s/aplicacao/mcpserver/deployment.yaml',
    'infra/k8s/aplicacao/mcpserver/service.yaml',
])

k8s_resource('precoapi',
    port_forwards=['8081:8080'],
    resource_deps=['postgres-preco', 'jaeger'],
)

k8s_resource('produtoapi',
    port_forwards=['8082:8080'],
    resource_deps=['postgres-produto', 'jaeger'],
)

k8s_resource('mcpserver',
    port_forwards=['4000:4000'],
    resource_deps=['precoapi', 'produtoapi'],
)
