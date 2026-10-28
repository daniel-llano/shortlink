import { test, expect } from "playwright/test";

test.describe("ShortLink", () => {
  test("shortens a link, counts clicks, and deletes it", async ({ page, request }) => {
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "ShortLink" })).toBeVisible();
    await expect(page.getByText("Links you create in this browser show up here.")).toBeVisible();

    await page.getByLabel("Long URL").fill("https://example.com/articles/2026/how-to-write-a-url-shortener");
    await page.getByRole("button", { name: "Shorten" }).click();

    const shortLink = page.locator("#short-url");
    await expect(shortLink).toHaveText(/http:\/\/localhost:5276\/r\/\w{7}/);
    await expect(page.getByAltText("QR code for the short link")).toBeVisible();
    await expect(page.locator("#owner-key")).not.toBeEmpty();

    const shortUrl = (await shortLink.textContent())!;
    const visit = await request.get(shortUrl, { maxRedirects: 0 });
    expect(visit.status()).toBe(302);
    expect(visit.headers()["location"]).toBe("https://example.com/articles/2026/how-to-write-a-url-shortener");

    await page.reload();
    const row = page.getByRole("row", { name: /how-to-write-a-url-shortener/ });
    await expect(row.getByRole("cell").nth(2)).toHaveText("1");

    page.once("dialog", (d) => d.accept());
    await row.getByRole("button", { name: "Delete" }).click();
    await expect(page.getByText("Links you create in this browser show up here.")).toBeVisible();
    expect((await request.get(shortUrl, { maxRedirects: 0 })).status()).toBe(404);
  });

  test("custom alias and validation errors", async ({ page }) => {
    const alias = `promo-${Date.now()}`;
    await page.goto("/");
    await page.getByLabel("Long URL").fill("https://example.org/spring");
    await page.getByText("Options").click();
    await page.getByLabel("Custom alias").fill(alias);
    await page.getByRole("button", { name: "Shorten" }).click();
    await expect(page.locator("#short-url")).toHaveText(`http://localhost:5276/r/${alias}`);

    await page.getByLabel("Long URL").fill("https://example.org/again");
    await page.getByLabel("Custom alias").fill(alias);
    await page.getByRole("button", { name: "Shorten" }).click();
    await expect(page.getByRole("alert")).toHaveText("Pick another alias.");

    await page.getByLabel("Long URL").fill("http://example.org/insecure");
    await page.getByLabel("Custom alias").fill("");
    await page.getByRole("button", { name: "Shorten" }).click();
    await expect(page.getByRole("alert")).toHaveText("Only https addresses are allowed.");
  });
});
