import http from 'k6/http'
import { check, sleep } from 'k6'

const baseUrl = (__ENV.BUSINESS_BASE_URL || 'http://127.0.0.1:5002').replace(/\/$/, '')
const paths = (__ENV.BUSINESS_PATHS || '/api/produtos').split(',').map((value) => value.trim())
const requestRate = Number(__ENV.REQUEST_RATE || 5)

export const options = {
  scenarios: {
    telemetry_seed: {
      executor: 'constant-arrival-rate',
      rate: requestRate,
      timeUnit: '1s',
      duration: __ENV.DURATION || '5m',
      preAllocatedVUs: Math.max(2, requestRate),
      maxVUs: Math.max(10, requestRate * 4),
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: [`p(95)<${Number(__ENV.P95_MS || 1000)}`],
    checks: ['rate>0.99'],
  },
}

export default function () {
  const path = paths[__ITER % paths.length]
  const response = http.get(`${baseUrl}${path}`, {
    headers: { 'X-Correlation-Id': `cluster-lab-${__VU}-${__ITER}` },
    tags: { traffic: 'business', path },
  })
  check(response, { 'business request succeeds': (result) => result.status < 500 })
  sleep(Number(__ENV.SLEEP_SECONDS || 0))
}
