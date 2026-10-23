// End-to-end smoke test and screenshot capture against a running API + frontend.
// Usage: BASE_URL=http://localhost:4173 node e2e/smoke.mjs
// Fails if any page logs an error, any /api request returns 5xx, or a key flow breaks.
import { chromium } from 'playwright'
import { mkdir } from 'node:fs/promises'

const base = process.env.BASE_URL ?? 'http://localhost:4173'
const out = process.env.SCREENSHOT_DIR ?? '../docs/screenshots'
const customer = { email: process.env.DEMO_CUSTOMER_EMAIL ?? 'customer@smartdrive.example', password: process.env.DEMO_CUSTOMER_PASSWORD }
const admin = { email: process.env.DEMO_ADMIN_EMAIL ?? 'admin@smartdrive.example', password: process.env.DEMO_ADMIN_PASSWORD }
if (!customer.password || !admin.password) throw new Error('Set DEMO_CUSTOMER_PASSWORD and DEMO_ADMIN_PASSWORD')

await mkdir(out, { recursive: true })
const browser = await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {})
const problems = []
const inCi = !!process.env.GITHUB_ACTIONS
const report = (message) => console.error(inCi ? `::error title=Smoke test::${message.replace(/\n/g, '%0A')}` : message)
let step = 'start'
const onFatal = (e) => {
  report(`Failed at "${step}": ${e?.message ?? e}${problems.length ? '\n' + problems.slice(0, 15).join('\n') : ''}`)
  process.exit(1)
}
process.on('unhandledRejection', onFatal)
process.on('uncaughtException', onFatal)

async function newPage(width = 1366, height = 900) {
  const page = await browser.newPage({ viewport: { width, height }, timezoneId: 'Africa/Johannesburg', locale: 'en-ZA' })
  page.on('console', (m) => { if (m.type() === 'error') problems.push(`console: ${m.text()}`) })
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`))
  page.on('response', (r) => { if (r.url().includes('/api/') && r.status() >= 500) problems.push(`${r.status()} ${r.url()}`) })
  return page
}

async function shot(page, name) {
  step = `after ${name}`
  await page.waitForLoadState('networkidle')
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: false })
  console.log(`✓ ${name}`)
}

async function signIn(page, who) {
  await page.goto(`${base}/login`)
  await page.getByLabel('Email').fill(who.email)
  await page.getByLabel('Password').fill(who.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.waitForURL((u) => !u.pathname.startsWith('/login'))
}

// ---------------------------------------------------------------- customer journey
const page = await newPage()
await page.goto(base)
await page.getByRole('heading', { name: 'Featured vehicles' }).waitFor()
await page.locator('article').first().waitFor()
await shot(page, '01-home')

await page.getByRole('button', { name: 'Show available cars' }).click()
await page.waitForURL(/\/vehicles\?/)
await page.locator('article').first().waitFor()
await shot(page, '02-browse')

await page.locator('article h3 a').first().click()
await page.getByRole('button', { name: 'Reserve this car' }).waitFor()
await shot(page, '03-vehicle')
await page.getByRole('tab', { name: /3D view/ }).click()
await page.locator('canvas').waitFor()
await page.waitForTimeout(1500)
await shot(page, '04-vehicle-3d')

await page.goto(`${base}/recommend`)
await page.getByLabel('Trip description').fill('A comfortable car for four adults for a five-day trip, no more than R900 per day')
await page.getByRole('button', { name: 'Interpret' }).click()
await page.getByText('What I understood').waitFor()
await shot(page, '05-wizard-interpret')
await page.getByRole('button', { name: /Use these preferences/ }).click()
await page.getByRole('button', { name: /Next/ }).click()
await page.getByRole('button', { name: /Recommend cars/ }).click()
await page.getByText('Match score').first().waitFor()
await shot(page, '06-wizard-results')

await signIn(page, customer)
await page.goto(base)
await page.getByRole('button', { name: 'Show available cars' }).click()
await page.locator('article h3 a').first().click()
await page.getByRole('button', { name: 'Reserve this car' }).click()
await page.getByRole('heading', { name: 'Review and reserve' }).waitFor()
await page.getByRole('checkbox', { name: /free cancellation ends/ }).check()
await shot(page, '07-checkout')
const reserve = page.getByRole('button', { name: /Reserve and continue/ })
if (await reserve.isDisabled()) {
  const alerts = await page.locator('[role=alert], [role=status]').allInnerTexts()
  throw new Error(`Reserve button disabled at ${page.url()} — ${alerts.join(' | ') || 'no alert shown'}`)
}
await reserve.click()
await page.getByRole('button', { name: /\(simulated\)/ }).waitFor()
await shot(page, '08-payment')
await page.getByRole('button', { name: /\(simulated\)/ }).click()
await page.getByText('Confirmed').first().waitFor()
await page.goto(`${base}/account/bookings`)
await page.locator('ul li a').first().waitFor()
await shot(page, '09-my-bookings')
await page.close()

// ---------------------------------------------------------------- admin
const adminPage = await newPage(1440, 900)
await signIn(adminPage, admin)
await adminPage.goto(`${base}/admin`)
await adminPage.getByText('Operations dashboard').waitFor()
await shot(adminPage, '10-admin-dashboard')
await adminPage.goto(`${base}/admin/insights`)
await adminPage.getByText('Smart Insights').first().waitFor()
await shot(adminPage, '11-insights')
for (const [tab, name] of [['Utilisation', '12-utilisation'], ['Revenue', '13-revenue'], ['Sentiment', '14-sentiment'], ['Forecast', '15-forecast']]) {
  await adminPage.getByRole('tab', { name: tab }).click()
  await adminPage.waitForTimeout(800)
  await shot(adminPage, name)
}
await adminPage.goto(`${base}/admin/fleet`)
await adminPage.locator('table tbody tr').first().waitFor()
await shot(adminPage, '16-fleet')
await adminPage.goto(`${base}/admin/maintenance`)
await adminPage.getByText('Service alerts').waitFor()
await shot(adminPage, '17-maintenance')
await adminPage.goto(`${base}/admin/reviews`)
await shot(adminPage, '18-reviews')
await adminPage.close()

// ---------------------------------------------------------------- mobile
const mobile = await newPage(390, 844)
await mobile.goto(base)
await mobile.locator('article').first().waitFor()
await shot(mobile, '19-mobile-home')
await mobile.close()

await browser.close()
if (problems.length) {
  report('Problems found:\n' + problems.slice(0, 30).join('\n'))
  process.exit(1)
}
console.log('Smoke test passed.')
