# Lumen

Client gratuito per l'API Google Gemini su **Windows 10 Mobile** (build 15063 e successive, ARM) e Windows 10/11 desktop (x64).
Client non ufficiale, non affiliato a Google. Gemini è un marchio di Google LLC.

## Funzioni

- Onboarding con un Lumia 735 in 3D (render three.js, video H.264 in loop) che fa girare Lumen
- Stile Windows Phone (Metro): pivot chat/cronologia, app bar in basso, tilt al tocco, titoli grandi e leggeri, bolle con la codina come in Messaggi, suggerimenti a live tile; icone Fluent System Icons (PNG mostrate con BitmapIcon)
- Immagini e file generati su richiesta (tool generate_image e create_file), con Salva, Apri, Condividi
- Lista chat con ricerca, anteprime, chat fissate, rinomina; menu a pressione lunga con icone
- Leggi ad alta voce, seleziona testo, elimina messaggio
- Notifiche in-app animate e live tile con l'ultima chat
- Risposte in streaming token per token, con pulsante stop
- Messaggi vocali (WAV 16 kHz mono, max 2 minuti) che Gemini ascolta e a cui risponde
- Foto dalla fotocamera o dalla galleria, ridimensionate a 1024 px prima dell'invio
- Ricerca automatica su Wikipedia via function calling (gratis, senza chiave) e data di oggi in ogni richiesta
- Temi: i 20 colori accent di Windows Phone più il colore del telefono, tema scuro o chiaro, dimensione testo, animazioni on/off
- Modelli solo da lista, caricata dalla tua chiave, con cambio rapido dal titolo della chat
- Markdown: titoli, elenchi, grassetto, corsivo, codice, link
- Storico chat salvato sul telefono, eliminazione con pressione lunga
- Pressione lunga su un messaggio: copia, condividi, rigenera, modifica
- Condivisione da altre app (testo, link, immagini) direttamente in una nuova chat
- Interfaccia in 6 lingue (EN, IT, ES, FR, DE, PT-BR), scelta nell'onboarding e nelle impostazioni

## Ottimizzazioni per i Lumia

- Nessuna dipendenza NuGet oltre al runtime UWP: HTTP, JSON e immagini usano solo API WinRT
- Durante lo streaming il testo viene aggiornato a circa 15 fps e il markdown viene elaborato una sola volta, a risposta finita
- Liste virtualizzate (`ItemsStackPanel`), binding compilati (`x:Bind`), immagini decodificate alla dimensione mostrata
- Nessun blur in tempo reale: l'effetto acrylic usa sfondi pre-sfocati, così la GPU del 735 non soffre
- Compilazione .NET Native in Release

## La chiave gratuita

1. Vai su https://aistudio.google.com/apikey con un account Google
2. Crea una chiave (non serve la carta)
3. In Lumen: Impostazioni, incolla la chiave, tocca "Verifica e carica modelli"

Il modello predefinito è `gemini-flash-lite-latest`, l'alias del Flash-Lite gratuito più recente. La Ricerca Google non fa parte del piano gratuito, per questo Lumen usa Wikipedia.

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
  Views/OnboardingPage     tour iniziale con il Lumia 735 animato
  Core/WikipediaClient.cs  ricerca Wikipedia esposta a Gemini come funzione
  Core/AudioRecorder.cs    registrazione vocale
  Core/ThemeManager.cs     temi live
  Core/Fx.cs               animazioni
  Core/FileTools.cs        generazione immagini e file
  Core/Speech.cs           lettura ad alta voce
  Core/TileHelper.cs       live tile
  Assets/Lumia735.mp4      video 3D dell'onboarding
  Assets/Icons/            Fluent System Icons (MIT) come PNG
  Strings/                 traduzioni resw
```
