import { expect, test, type Locator, type Page, type Route } from '@playwright/test'

const now = '2026-08-07T14:30:00Z'
const window30m = {
  from: '2026-08-07T14:00:00Z',
  to: now,
  duration: 'PT30M',
  timezone: 'UTC',
}
const orders = {
  serviceName: 'orders-api',
  namespace: 'mcp-apis',
  deploymentName: 'orders-api',
  kubernetesServiceName: 'orders-api',
  otelServiceName: 'orders-api',
  metricsId: 'orders-api',
  aliases: ['orders-api'],
}
const catalog = {
  ...orders,
  serviceName: 'catalog-api',
  deploymentName: 'catalog-api',
  kubernetesServiceName: 'catalog-api',
  otelServiceName: 'catalog-api',
  metricsId: 'catalog-api',
  aliases: ['catalog-api'],
}
const sources = [
  {
    name: 'Kubernetes',
    availability: 'available',
    observedAt: now,
    freshnessSeconds: 2,
    elapsedMilliseconds: 12,
    warnings: [],
  },
  {
    name: 'Prometheus',
    availability: 'available',
    observedAt: now,
    freshnessSeconds: 5,
    elapsedMilliseconds: 31,
    warnings: [],
  },
  {
    name: 'Jaeger',
    availability: 'stale',
    observedAt: '2026-08-07T14:28:30Z',
    freshnessSeconds: 90,
    elapsedMilliseconds: 44,
    warnings: ['Traces recentes com cobertura parcial.'],
  },
]

const summaries = [
  {
    service: orders,
    healthStatus: 'critical',
    score: 42,
    coverage: 0.83,
    criticalFindings: 1,
    evaluatedAt: now,
  },
  {
    service: catalog,
    healthStatus: 'healthy',
    score: 96,
    coverage: 1,
    criticalFindings: 0,
    evaluatedAt: now,
  },
]

const coverage = {
  kubernetes: 'available',
  metrics: 'available',
  traces: 'stale',
  logs: 'available',
  openApi: 'available',
  events: 'available',
}

const applications = [
  {
    name: 'orders-api',
    namespace: 'mcp-apis',
    sources: ['deployment', 'network', 'otel', 'config'],
    deploymentName: 'orders-api',
    kubernetesServiceName: 'orders-api',
    otelServiceName: 'orders-api',
    baseUrl: 'http://orders-api.mcp-apis.svc:8080',
    selector: { app: 'orders-api' },
    image: 'orders-api:2.1.0',
    imageDigest: 'sha256:0123456789abcdef',
    version: '2.1.0',
    revision: '17',
    desiredReplicas: 2,
    readyReplicas: 1,
    owner: 'platform',
    team: 'commerce',
    description: 'Processamento de pedidos.',
    coverage,
    declaredDependencies: ['catalog-api'],
    hasReadyEndpoints: true,
    openApi: { validated: true, path: '/swagger/v1/swagger.json', failures: [] },
    enabled: true,
    lockedDisabled: false,
    firstSeen: '2026-07-01T10:00:00Z',
    lastSeen: now,
    missing: false,
    health: summaries[0],
  },
  {
    name: 'catalog-api',
    namespace: 'mcp-apis',
    sources: ['deployment', 'network', 'otel'],
    deploymentName: 'catalog-api',
    kubernetesServiceName: 'catalog-api',
    otelServiceName: 'catalog-api',
    baseUrl: 'http://catalog-api.mcp-apis.svc:8080',
    selector: { app: 'catalog-api' },
    image: 'catalog-api:1.8.2',
    imageDigest: null,
    version: '1.8.2',
    revision: '8',
    desiredReplicas: 2,
    readyReplicas: 2,
    owner: 'platform',
    team: 'catalog',
    description: 'Catálogo de produtos.',
    coverage: { ...coverage, traces: 'available' },
    declaredDependencies: [],
    hasReadyEndpoints: true,
    openApi: { validated: true, path: '/swagger/v1/swagger.json', failures: [] },
    enabled: true,
    lockedDisabled: false,
    firstSeen: '2026-07-01T10:00:00Z',
    lastSeen: now,
    missing: false,
    health: summaries[1],
  },
]

function envelope(data: unknown) {
  return {
    schemaVersion: '2.0',
    generatedAt: now,
    executionStatus: 'partial',
    service: orders,
    window: window30m,
    data,
    sources,
    warnings: ['Jaeger com freshness reduzida.'],
    evidence: [],
    error: null,
  }
}

const responses: Record<string, unknown> = {
  overview: {
    generatedAt: now,
    window: window30m,
    system: {
      healthStatus: 'critical',
      totalServices: 2,
      healthy: 1,
      degraded: 0,
      critical: 1,
      unknown: 0,
      services: summaries,
      evaluatedAt: now,
    },
    sources,
    warnings: ['Jaeger com freshness reduzida.'],
    cluster: { totalPods: 4, readyPods: 3, totalDeployments: 2, readyDeployments: 1 },
    links: { Prometheus: 'http://localhost:9090', Jaeger: 'http://localhost:16686' },
  },
  applications: {
    generatedAt: now,
    lastScanAt: now,
    healthWindow: window30m,
    sources,
    warnings: [],
    canManage: true,
    applications,
  },
  health: envelope({
    healthStatus: 'critical',
    score: 42,
    coverage: 0.83,
    dimensions: [
      { name: 'availability', weight: 0.35, score: 50, required: true, availability: 'available', evidenceIds: ['ev-k8s'] },
      { name: 'errors', weight: 0.3, score: 22, required: true, availability: 'available', evidenceIds: ['ev-errors'] },
      { name: 'latency', weight: 0.2, score: 41, required: true, availability: 'available', evidenceIds: ['ev-latency'] },
      { name: 'resources', weight: 0.15, score: 78, required: false, availability: 'available', evidenceIds: ['ev-cpu'] },
    ],
    findings: [
      {
        type: 'elevatedErrorRate',
        severity: 'critical',
        service: orders,
        message: 'A taxa de erros 5xx subiu para 18%.',
        startedAt: '2026-08-07T14:12:00Z',
        evidenceIds: ['ev-errors'],
      },
    ],
    evaluatedAt: now,
  }),
  anomalies: envelope({
    analysisConclusion: 'detected',
    anomalies: [
      {
        metric: 'httpErrorRate',
        conclusion: 'detected',
        severity: 'critical',
        currentValue: 0.18,
        expectedValue: 0.01,
        deviation: 17,
        unit: 'ratio',
        method: 'robust-z-score',
        sampleCount: 60,
        estimatedStart: '2026-08-07T14:12:00Z',
        evidenceIds: ['ev-errors'],
      },
    ],
    evaluatedAt: now,
  }),
  spec: envelope({
    description: 'Processamento de pedidos.',
    owner: 'platform',
    team: 'commerce',
    version: '2.1.0',
    image: 'orders-api:2.1.0',
    imageDigest: 'sha256:0123456789abcdef',
    revision: '17',
    desiredReplicas: 2,
    readyReplicas: 1,
    labels: { app: 'orders-api' },
    annotations: {},
    selector: { app: 'orders-api' },
    coverage,
    endpoints: [
      { method: 'POST', path: '/api/orders', summary: 'Cria pedido', operationId: 'createOrder', responseCodes: ['201', '400'] },
    ],
    declaredDependencies: ['catalog-api'],
  }),
  dependencies: envelope({
    root: orders,
    depth: 2,
    nodes: [orders, catalog],
    inbound: [],
    outbound: [
      {
        source: orders,
        target: catalog,
        type: 'http',
        observedAt: now,
        callCount: 148,
        errorRate: 0.12,
        latencyMilliseconds: 312,
        evidenceIds: ['ev-trace'],
        declared: true,
        observed: true,
      },
    ],
    cycles: [],
    criticalPath: ['orders-api', 'catalog-api'],
    potentialBlastRadius: ['checkout-api'],
  }),
  timeline: envelope({
    analysisConclusion: 'detected',
    incidentStartedAt: '2026-08-07T14:12:00Z',
    events: [
      {
        id: 'deploy-17',
        timestamp: '2026-08-07T14:08:00Z',
        type: 'deploymentRevisionChanged',
        service: orders,
        severity: 'info',
        source: 'deployments',
        summary: 'Revision 17 implantada.',
        evidenceIds: ['ev-deploy'],
      },
      {
        id: 'anomaly-errors',
        timestamp: '2026-08-07T14:12:00Z',
        type: 'elevatedErrorRate',
        service: orders,
        severity: 'critical',
        source: 'metrics',
        summary: 'Erros 5xx aumentaram após o deploy.',
        evidenceIds: ['ev-errors'],
      },
    ],
    correlations: ['deploy-17 precede anomaly-errors em 4 minutos'],
  }),
  'root-cause': envelope({
    analysisConclusion: 'detected',
    primaryHypothesis: {
      id: 'deploy-regression',
      summary: 'A revisão 17 precedeu a regressão de erros e latência.',
      confidence: 0.86,
      supportingEvidenceIds: ['ev-deploy', 'ev-errors', 'ev-latency'],
      contradictingEvidenceIds: [],
      potentiallyAffectedServices: ['checkout-api'],
    },
    alternatives: [],
    coverage: 0.83,
    limitations: ['Traces recentes estão parcialmente stale.'],
    recommendations: [
      {
        priority: 'P1',
        action: 'Comparar a revisão 17 com a revisão anterior.',
        reason: 'O início da regressão ocorreu quatro minutos depois do deploy.',
        evidenceIds: ['ev-deploy', 'ev-errors'],
        executable: false,
      },
    ],
  }),
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}

async function mockDashboard(page: Page, canManage = true) {
  await page.route('**/api/dashboard/**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/overview')) return fulfillJson(route, responses.overview)
    if (url.pathname.endsWith('/applications')) {
      return fulfillJson(route, { ...(responses.applications as object), canManage })
    }
    const capability = url.pathname.split('/').at(-1) ?? ''
    if (capability in responses) return fulfillJson(route, responses[capability])
    await route.fulfill({ status: 404, body: `Unexpected dashboard route: ${url.pathname}` })
  })
}

function captureRuntimeErrors(page: Page) {
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text())
  })
  return errors
}

async function expectFullyOpaque(locator: Locator) {
  await expect.poll(async () =>
    Number(await locator.evaluate((element) => getComputedStyle(element).opacity)),
  ).toBeGreaterThan(0.98)
}

test('navega por health, anomalias, dependências, timeline e RCA sem expor queries raw', async ({ page }, testInfo) => {
  const runtimeErrors = captureRuntimeErrors(page)
  await mockDashboard(page)
  await page.goto('./')

  await expect(page.getByRole('heading', { name: 'Saúde consolidada' })).toBeVisible()
  await expect(page.getByText('Sistema crítico')).toBeVisible()
  await expect(page.getByRole('row', { name: /orders-api/ })).toContainText('42')
  await page.screenshot({ path: testInfo.outputPath('desktop-overview.png'), fullPage: true })

  await page.getByRole('row', { name: /orders-api/ }).getByRole('button', { name: 'Analisar' }).click()
  await expect(page.getByRole('heading', { name: /Health Engine/ })).toBeVisible()
  await expect(page.getByRole('heading', { name: /Anomaly Engine/ })).toBeVisible()
  await expect(page.getByRole('heading', { name: /Dependency Engine/ })).toBeVisible()
  await expect(page.getByText('A taxa de erros 5xx subiu para 18%.')).toBeVisible()
  const criticalPath = page.getByText('Caminho crítico observado').locator('..')
  await expect(criticalPath).toContainText('orders-api')
  await expect(criticalPath).toContainText('catalog-api')

  await page.getByRole('tab', { name: 'Incidente' }).click()
  await expect(page.getByRole('heading', { name: /Timeline correlacionada/ })).toBeVisible()
  await expect(page.getByRole('heading', { name: /RCA explicável/ })).toBeVisible()
  await expect(page.getByText('A revisão 17 precedeu a regressão de erros e latência.')).toBeVisible()
  await expect(page.getByText('somente leitura')).toBeVisible()
  await expect(page.getByText('Revision 17 implantada.')).toBeVisible()
  await expect(page.getByText(/PromQL personalizada/i)).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('desktop-incident.png'), fullPage: true })

  await page.getByRole('tab', { name: 'Projeto' }).click()
  const heroHeading = page.getByRole('heading', { name: /Um radar sobre o cluster/ })
  await expect(heroHeading).toBeVisible()
  await expectFullyOpaque(heroHeading)
  await page.locator('section').filter({ has: heroHeading }).screenshot({
    path: testInfo.outputPath('desktop-project-hero.png'),
  })

  const installHeading = page.getByRole('heading', { name: 'Do zero ao radar no cluster em uma linha' })
  await installHeading.scrollIntoViewIfNeeded()
  await expect(installHeading).toBeVisible()
  await expectFullyOpaque(installHeading.locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  await page.getByRole('button', { name: 'PowerShell' }).click()
  await expect(page.getByText(/install\.ps1 \| iex/)).toBeVisible()
  await expectFullyOpaque(page.locator('section[aria-labelledby="instalar-heading"] > div.opacity-0').last())
  await page.locator('section[aria-labelledby="instalar-heading"]').screenshot({
    path: testInfo.outputPath('desktop-project-install.png'),
  })

  const signalsHeading = page.getByRole('heading', { name: 'Veja o que cada fonte acrescenta à investigação' })
  await signalsHeading.scrollIntoViewIfNeeded()
  await expectFullyOpaque(signalsHeading.locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  await page.getByRole('button', { name: /Cluster Kubernetes/ }).click()
  await expect(page.getByText('A regressão apareceu quatro minutos após o deploy da revisão 17.')).toBeVisible()
  await expectFullyOpaque(page.locator('#signal-detail').locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  await page.locator('section[aria-labelledby="sinais-heading"]').screenshot({
    path: testInfo.outputPath('desktop-project-signals.png'),
  })

  const practiceHeading = page.getByRole('heading', { name: 'Escolha uma pergunta e veja como a resposta é construída' })
  await practiceHeading.scrollIntoViewIfNeeded()
  await expectFullyOpaque(practiceHeading.locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  await page.getByRole('button', { name: /Causa provável/ }).click()
  await expect(page.getByText(/Hipótese principal, confiança 86%/)).toBeVisible()
  await expect(page.getByText(/recomendação é somente leitura/)).toBeVisible()
  await page.locator('section[aria-labelledby="chat-heading"]').screenshot({
    path: testInfo.outputPath('desktop-project-practice.png'),
  })

  const toolsHeading = page.getByRole('heading', { name: '8 tools vNext para investigação orientada por evidências' })
  await toolsHeading.scrollIntoViewIfNeeded()
  await expect(toolsHeading).toBeVisible()
  await expect(page.getByText('PromQL arbitrário liberado ao agente.')).toHaveCount(0)
  const toolsSection = page.locator('section[aria-labelledby="ferramentas-heading"]')
  const toolNames = [
    'service_get_spec',
    'service_get_health',
    'service_get_score',
    'service_get_dependencies',
    'service_detect_anomalies',
    'service_get_incident_timeline',
    'service_find_root_cause',
    'system_get_health_summary',
  ]
  for (const name of toolNames) {
    const tool = toolsSection.getByText(name, { exact: true })
    await tool.scrollIntoViewIfNeeded()
    await expectFullyOpaque(tool.locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  }
  await toolsSection.screenshot({
    path: testInfo.outputPath('desktop-project-tools.png'),
  })

  const rawDisabled = page.getByText('raw queries desabilitadas')
  await rawDisabled.scrollIntoViewIfNeeded()
  await expect(rawDisabled).toBeVisible()
  await expectFullyOpaque(rawDisabled.locator('xpath=ancestor::div[contains(@class,"opacity-0")][1]'))
  await page.locator('section[aria-labelledby="arquitetura-heading"]').screenshot({
    path: testInfo.outputPath('desktop-project-architecture.png'),
  })

  expect(runtimeErrors).toEqual([])
})

test('mantém navegação por teclado, tema escuro, touch targets e layout móvel', async ({ page }, testInfo) => {
  const runtimeErrors = captureRuntimeErrors(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await mockDashboard(page, false)
  await page.goto('./')

  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Pular para o conteúdo' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.locator('#main')).toBeFocused()

  await page.getByRole('button', { name: 'Mudar para tema escuro' }).click()
  await expect(page.locator('html')).toHaveClass(/dark/)
  await expect.poll(() => page.evaluate(() => localStorage.getItem('mcp-dashboard-theme'))).toBe('dark')

  await page.getByRole('tab', { name: 'Serviço' }).click()
  await expect(page.getByRole('heading', { name: /Health Engine/ })).toBeVisible()

  const unnamedButtons = await page.getByRole('button').evaluateAll((buttons) =>
    buttons
      .filter((button) => {
        const element = button as HTMLElement
        return !element.innerText.trim() && !element.getAttribute('aria-label') && !element.getAttribute('title')
      })
      .map((button) => (button as HTMLElement).outerHTML),
  )
  expect(unnamedButtons).toEqual([])

  const undersizedTargets = await page.getByRole('button').evaluateAll((buttons) =>
    buttons
      .filter((button) => {
        const rect = (button as HTMLElement).getBoundingClientRect()
        return rect.width > 0 && rect.height > 0 && (rect.width < 44 || rect.height < 44)
      })
      .map((button) => ({
        label: button.getAttribute('aria-label') ?? (button as HTMLElement).innerText.trim(),
        rect: (button as HTMLElement).getBoundingClientRect().toJSON(),
      })),
  )
  expect(undersizedTargets).toEqual([])

  const layoutWidths = await page.evaluate(() => ({
    documentClient: document.documentElement.clientWidth,
    documentScroll: document.documentElement.scrollWidth,
    bodyClient: document.body.clientWidth,
    bodyScroll: document.body.scrollWidth,
  }))
  expect(layoutWidths.documentScroll).toBe(layoutWidths.documentClient)
  expect(layoutWidths.bodyScroll).toBe(layoutWidths.bodyClient)
  await page.screenshot({ path: testInfo.outputPath('mobile-service-dark.png'), fullPage: true })

  await page.getByRole('tab', { name: 'Catálogo' }).click()
  await expect(page.getByRole('button', { name: 'Re-scan' })).toBeDisabled()
  const switches = page.getByRole('switch')
  await expect(switches).toHaveCount(2)
  for (const toggle of await switches.all()) await expect(toggle).toBeDisabled()

  await page.getByRole('tab', { name: 'Projeto' }).click()
  const mobileInstall = page.locator('section[aria-labelledby="instalar-heading"]')
  await mobileInstall.scrollIntoViewIfNeeded()
  await expect(mobileInstall).toBeVisible()
  for (const reveal of await mobileInstall.locator('div.opacity-0').all()) await expectFullyOpaque(reveal)
  await mobileInstall.screenshot({ path: testInfo.outputPath('mobile-project-install-dark.png') })

  const projectWidths = await page.evaluate(() => ({
    documentClient: document.documentElement.clientWidth,
    documentScroll: document.documentElement.scrollWidth,
    bodyClient: document.body.clientWidth,
    bodyScroll: document.body.scrollWidth,
  }))
  expect(projectWidths.documentScroll).toBe(projectWidths.documentClient)
  expect(projectWidths.bodyScroll).toBe(projectWidths.bodyClient)

  expect(runtimeErrors).toEqual([])
})
