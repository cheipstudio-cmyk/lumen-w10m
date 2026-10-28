# Lumen

Client gratuito per l'API Google Gemini su **Windows 10 Mobile** (build 15063 e successive, ARM) e Windows 10/11 desktop (x64).
Client non ufficiale, non affiliato a Google. Gemini è un marchio di Google LLC.

## Funzioni

- Risposte in streaming token per token, con pulsante stop
- Foto dalla galleria o dalla fotocamera: ridimensionate a 1024 px prima dell'invio
- Markdown: titoli, elenchi, grassetto, corsivo, codice, link
- Storico chat salvato sul telefono, menu laterale, eliminazione con pressione lunga
- Pressione lunga su un messaggio: copia, condividi, rigenera, modifica
- Condivisione da altre app (testo, link, immagini) direttamente in una nuova chat
- Ricerca Google opzionale per risposte aggiornate
- Scelta del modello con lista caricata dalla tua chiave
- Interfaccia in italiano e inglese

## Ottimizzazioni per i Lumia

- Nessuna dipendenza NuGet oltre al runtime UWP: HTTP, JSON e immagini usano solo API WinRT
- Durante lo streaming il testo viene aggiornato a circa 15 fps e il markdown viene elaborato una sola volta, a risposta finita
- Liste virtualizzate (`ItemsStackPanel`), binding compilati (`x:Bind`), immagini decodificate alla dimensione mostrata
- Nessun blur, acrylic o ombra
- Compilazione .NET Native in Release

## La chiave gratuita

1. Vai su https://aistudio.google.com/apikey con un account Google
2. Crea una chiave (non serve la carta)
3. In Lumen: Impostazioni, incolla la chiave, tocca "Verifica e carica modelli"

Il modello predefinito è `gemini-flash-latest`, un alias che punta sempre al Flash gratuito più recente. Se finisci la quota giornaliera, passa a un modello Flash-Lite.

## Build

Non serve Visual Studio in locale. Il workflow `.github/workflows/build.yml` compila su GitHub Actions:

```
git tag v1.0.0
git push origin v1.0.0
```

Il risultato è nella release del tag (oppure negli artifact del workflow):

- `Lumen_x.y.z.0.appxbundle`: ARM per il telefono, x64 per il PC
- `Lumen.cer`: certificato da installare prima dell'app
- `Dependencies/ARM/*.appx`: .NET Native Framework 2.2, .NET Native Runtime 2.2, VCLibs

### Certificato stabile (consigliato)

Senza segreti il workflow genera un certificato nuovo a ogni build, quindi a ogni aggiornamento devi reinstallare anche il `.cer`.
Per usare sempre lo stesso, crea una volta un PFX con soggetto `CN=Second Dream` e salvalo nei secret del repo:

- `SIGNING_PFX`: il file .pfx in base64
- `SIGNING_PASSWORD`: la password del pfx

## Installazione su Windows 10 Mobile

1. Telefono: Impostazioni > Aggiornamento e sicurezza > Per sviluppatori > **Modalità sviluppatore**
2. Copia sul telefono `Lumen.cer`, la cartella `Dependencies/ARM` e l'appxbundle
3. Da Esplora file tocca `Lumen.cer` e installalo
4. Tocca ogni `.appx` in `Dependencies/ARM` (se una dice che è già installata, va bene)
5. Tocca l'appxbundle

In alternativa: Device Portal (Per sviluppatori > Individuazione dispositivi + Portale dispositivi), poi dal browser del PC vai su Apps > Install app, carica l'appxbundle e aggiungi le dipendenze.

## Se il telefono non si connette

Se compare l'errore sulla connessione sicura, il telefono non riesce a validare il certificato TLS di Google. In Impostazioni > Avanzate puoi mettere l'URL di un tuo proxy (per esempio un Cloudflare Worker che inoltra a `https://generativelanguage.googleapis.com`). La chiave viaggia nell'header `x-goog-api-key`, quindi il proxy deve inoltrarlo.

## Struttura

```
Lumen/
  App.xaml(.cs)            avvio, tasto indietro, share target
  MainPage.xaml(.cs)       chat, menu laterale, composer
  Views/SettingsPage       chiave, modello, creatività, ricerca, prompt, proxy
  Core/GeminiClient.cs     REST + SSE streaming, lista modelli, errori leggibili
  Core/ChatStore.cs        salvataggio JSON in LocalFolder
  Core/ImageHelper.cs      picker, ridimensionamento JPEG
  Core/Markdown.cs         renderer markdown per RichTextBlock
  Core/AppSettings.cs      impostazioni + chiave nel PasswordVault
  Strings/                 en-US, it-IT
```
