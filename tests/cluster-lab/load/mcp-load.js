import http from 'k6/http'
import { check, sleep } from 'k6'
import { Rate } from 'k6/metrics'

const baseUrl = (__ENV.BASE_URL || 'http://127.0.0.1:4000').replace(/\/$/, '')
const profile = __ENV.LOAD_PROFILE || 'smoke'
const p95Milliseconds = Number(__ENV.P95_MS || 2000)
const maximumErrorRate = Number(__ENV.MAX_ERROR_RATE || 0.01)
const requestRate = Number(__ENV.REQUEST_RATE || 5)

const profiles = {
  smoke: {
    executor: 'constant-arrival-rate',
    rate: Math.min(requestRate, 5),
    timeUnit: '1s',
    duration: '30s',
    preAllocatedVUs: 2,
    maxVUs: 10,
  },
  average: {
    executor: 'constant-arrival-rate',
    rate: requestRate,
    timeUnit: '1s',
    duration: __ENV.DURATION || '10m',
    preAllocatedVUs: Math.max(5, requestRate),
    maxVUs: Math.max(20, requestRate * 4),
  },
  spike: {
    executor: 'ramping-arrival-rate',
    startRate: 1,
    timeUnit: '1s',
    preAllocatedVUs: 20,
    maxVUs: 200,
    stages: [
      { target: requestRate, duration: '30s' },
      { target: requestRate * 5, duration: '30s' },
      { target: requestRate, duration: '30s' },
    ],
  },
  soak: {
    executor: 'constant-arrival-rate',
    rate: requestRate,
    timeUnit: '1s',
    duration: __ENV.DURATION || '2h',
    preAllocatedVUs: Math.max(5, requestRate),
    maxVUs: Math.max(20, requestRate * 4),
  },
}

if (!profiles[profile]) throw new Error(`Unknown LOAD_PROFILE: ${profile}`)

export const options = {
  scenarios: { mcp: profiles[profile] },
  thresholds: {
    mcp_errors: [`rate<${maximumErrorRate}`],
    checks: ['rate>0.99'],
    http_req_duration: [`p(95)<${p95Milliseconds}`],
    dropped_iterations: ['count==0'],
  },
}

const errors = new Rate('mcp_errors')
let sessionId = ''
let nextId = 1

const baseHeaders = {
  'Content-Type': 'application/json',
  Accept: 'application/json, text/event-stream',
}

function post(payload, tags, withSession = true) {
  const headers = { ...baseHeaders }
  if (withSession && sessionId) headers['Mcp-Session-Id'] = sessionId
  return http.post(baseUrl, JSON.stringify(payload), { headers, tags })
}

function initialize() {
  const response = post(
    {
      jsonrpc: '2.0',
      id: nextId++,
      method: 'initialize',
      params: {
        protocolVersion: '2025-03-26',
        capabilities: {},
        clientInfo: { name: 'cluster-lab-k6', version: '1.0' },
      },
    },
    { operation: 'initialize' },
    false,
  )
  const ok = check(response, {
    'initialize returns 200': (result) => result.status === 200,
    'initialize identifies MCP server': (result) => result.body.includes('mcp-apis-server'),
  })
  sessionId = response.headers['Mcp-Session-Id'] || response.headers['Mcp-session-id'] || ''
  errors.add(!ok || !sessionId)
  if (!ok || !sessionId) return false

  const notification = post(
    { jsonrpc: '2.0', method: 'notifications/initialized' },
    { operation: 'initialized' },
  )
  errors.add(notification.status !== 200 && notification.status !== 202)
  return true
}

export default function () {
  if (!sessionId && !initialize()) {
    sleep(1)
    return
  }

  const list = post(
    { jsonrpc: '2.0', id: nextId++, method: 'tools/list' },
    { operation: 'tools_list' },
  )
  const listOk = check(list, {
    'tools/list returns 200': (result) => result.status === 200,
    'tools/list contains vNext tools': (result) => result.body.includes('system_get_health_summary'),
  })
  errors.add(!listOk)

  const summary = post(
    {
      jsonrpc: '2.0',
      id: nextId++,
      method: 'tools/call',
      params: { name: 'system_get_health_summary', arguments: { windowMinutes: 5 } },
    },
    { operation: 'system_health' },
  )
  const summaryOk = check(summary, {
    'system health returns 200': (result) => result.status === 200,
    'system health has a versioned envelope': (result) => result.body.includes('schemaVersion'),
  })
  errors.add(!summaryOk)

  if (list.status === 404 || summary.status === 404) sessionId = ''
}
