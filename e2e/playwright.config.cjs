const { defineConfig, devices } = require("playwright/test");

// Runs against the Docker stack from infra/docker-compose.yml.
module.exports = defineConfig({
  testDir: __dirname,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "../playwright-report" }]],
  use: {
    ...devices["Desktop Chrome"],
    baseURL: process.env.APP_URL || "http://localhost:5276",
    screenshot: "only-on-failure",
    trace: "on-first-retry",
  },
});
