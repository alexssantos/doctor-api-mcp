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
    'infra/k8s/postgres-preco/secret.yaml',
    'infra/k8s/postgres-preco/configmap-init.yaml',
    'infra/k8s/postgres-preco/service.yaml',
    'infra/k8s/postgres-preco/statefulset.yaml',
    # Postgres - Produto
    'infra/k8s/postgres-produto/secret.yaml',
    'infra/k8s/postgres-produto/configmap-init.yaml',
    'infra/k8s/postgres-produto/service.yaml',
    'infra/k8s/postgres-produto/statefulset.yaml',
    # Jaeger
    'infra/k8s/jaeger/deployment.yaml',
    'infra/k8s/jaeger/service.yaml',
    # Prometheus
    'infra/k8s/prometheus/configmap.yaml',
    'infra/k8s/prometheus/deployment.yaml',
    'infra/k8s/prometheus/service.yaml',
    'infra/k8s/prometheus/ingress.yaml',
    # Loki
    'infra/k8s/loki/configmap.yaml',
    'infra/k8s/loki/deployment.yaml',
    'infra/k8s/loki/service.yaml',
    # Promtail
    'infra/k8s/promtail/configmap.yaml',
    'infra/k8s/promtail/daemonset.yaml',
    # Grafana
    'infra/k8s/grafana/configmap-datasources.yaml',
    'infra/k8s/grafana/deployment.yaml',
    'infra/k8s/grafana/service.yaml',
    'infra/k8s/grafana/ingress.yaml',
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
    'infra/k8s/precoapi/secret.yaml',
    'infra/k8s/precoapi/configmap.yaml',
    'infra/k8s/precoapi/deployment.yaml',
    'infra/k8s/precoapi/service.yaml',
    'infra/k8s/precoapi/ingress.yaml',
    # ProdutoAPI
    'infra/k8s/produtoapi/secret.yaml',
    'infra/k8s/produtoapi/configmap.yaml',
    'infra/k8s/produtoapi/deployment.yaml',
    'infra/k8s/produtoapi/service.yaml',
    'infra/k8s/produtoapi/ingress.yaml',
    # McpServer
    'infra/k8s/mcpserver/rbac.yaml',
    'infra/k8s/mcpserver/configmap.yaml',
    'infra/k8s/mcpserver/deployment.yaml',
    'infra/k8s/mcpserver/service.yaml',
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
