# Runbook: Instagram op de homepage

De homepage toont de laatste 6 posts van Instagram in de huisstijl (raster met foto, bijschrift en een ▶ bij video's).
De server haalt ze op via de Graph API van Facebook. Bezoekers laden geen script of cookies van Instagram, alleen de
afbeeldingen. Zonder token of koppeling staat er alleen de link naar Instagram (Beheer → Website → Instellingen →
Instagram).

## 1. Instagram koppelen aan de Facebookpagina

1. In de Instagram-app: **Instellingen → Accounttype en tools → Overstappen naar een professioneel account**. Kies
   Bedrijf of Maker; dat is gratis.
2. Koppel het account aan de Facebookpagina van De Vrolijke Drammers. In de Instagram-app gaat dat via
   **Instellingen → Accountcentrum → Accounts toevoegen**. Op Facebook gaat het via **Pagina-instellingen →
   Gekoppelde accounts → Instagram**.

## 2. Paginatoken met Instagram-rechten

In de app op developers.facebook.com, bij de use case **Manage everything on your Page**:

1. Open de **Graph API Explorer** en kies de app.
2. Voeg de rechten toe:
   - `pages_show_list`
   - `pages_read_engagement`
   - `instagram_basic`
3. Klik op **Generate Access Token**.
4. Maak het token langdurig: klik op het i-icoon → **Open in Access Token Tool** → **Extend Access Token**.
5. Plak het lange token in de Explorer en voer `me/accounts` uit. Het `access_token` bij De Vrolijke Drammers is het
   paginatoken; het verloopt niet.
6. Controle in de Explorer:
   - `263171860384920?fields=instagram_business_account` geeft een `id` terug;
   - `<dat id>/media?fields=caption,media_url,permalink` toont de posts.

## 3. Token instellen

```
infra/prod/set-secret.sh prod facebook-page-token
```

Plak het paginatoken als het script erom vraagt. De naam blijft `facebook-page-token`: het is een token van de
Facebookpagina. Binnen een half uur staan de posts op de homepage.
