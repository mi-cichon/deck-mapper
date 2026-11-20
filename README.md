# DeckMapper

Mapuje wbudowany kontroler Steam Decka (Windows) na klawiaturę i mysz. Działa bez Steama, z zasobnika.
Ma własną klawiaturę ekranową, która nie zabiera grze fokusu.

## Budowanie (Mac, Rider albo terminal)

```bash
dotnet publish -c Release
```

Wynik: `bin/Release/net8.0-windows/win-x64/publish/DeckMapper.exe`, jeden plik, bez instalowania .NET na Decku.
Na macOS się nie uruchomi (to WinForms), ale zbuduje się dzięki `EnableWindowsTargeting`.

## Uruchomienie na Decku

1. Skopiuj `DeckMapper.exe` do jakiegoś folderu (np. `C:\Tools\DeckMapper`).
2. **Zamknij Steama całkowicie.** Gdy Steam działa, przejmuje kontroler i wyłącza lizard mode.
3. Odpal `DeckMapper.exe`. Obok utworzy się folder `profiles/` z profilem `MTA.json` (domyślne mapowanie pod MTA/GTA SA)
   i plik `settings.json`. Jeśli obok leżał stary `mapping.json`, zostanie przeniesiony do `profiles/MTA.json`
   (oryginał zostaje jako `mapping.json.bak`).
4. W MTA ustaw tryb wyświetlania na **okno bez ramki (borderless)**, inaczej klawiatura ekranowa nie będzie widoczna nad grą.

Autostart: skrót do exe w `shell:startup` (Win+R).

## Ikonka w zasobniku

- **Lewy klik**: klawiatura ekranowa
- **Prawy klik**: menu: profil (podmenu z listą, następny profil, automatyczne przełączanie), edytor mapowania,
  klawiatura, wstrzymaj, podgląd kontrolera, edycja pliku profilu w Notatniku, folder profili, przeładuj, zamknij

## Profile

- Każdy profil to osobny plik w `profiles/` (np. `profiles/MTA.json`), w tym samym formacie co dawny `mapping.json`.
- `settings.json` pamięta aktywny profil i reguły automatycznego przełączania.
- Przełączanie: menu ikonki → **Profil**, przycisk **Ustaw jako aktywny** w edytorze albo akcje
  `@Profile:Nazwa` / `@NextProfile` przypisane do przycisku. Przy zmianie profilu pokazuje się dymek.
- **Automatyczne przełączanie** (edytor → *Auto-przełączanie…*): co ~1 s sprawdzany jest proces okna na pierwszym
  planie, np. `gta_sa.exe` → `MTA`. Profil zmienia się tylko przy zmianie okna, więc ręczny wybór w trakcie gry
  zostaje. Można też ustawić profil „gdy nic nie pasuje”. Domyślnie wyłączone.
- Ręczna edycja pliku dalej działa (komentarze `//` są dozwolone) – aktywny profil przeładowuje się sam po zapisaniu.
  Zapis z edytora formatuje plik od nowa (komentarze przy przypisaniach znikają, ściąga na górze zostaje).

## Edytor mapowania

Menu ikonki → **Edytor mapowania…** albo akcja `@Editor` (w domyślnym profilu: trzymaj L4 + Quick Access).
Jest zrobiony pod ekran dotykowy Decka: duże przyciski, listy przewijane palcem, tekst wpisywany klawiaturą ekranową.

- **Na czas edycji mapowanie jest wstrzymane** – przyciski Decka nic nie wysyłają. Po zamknięciu edytora wraca.
- **Wciśnij przycisk na Decku**, a jego wiersz się zaznaczy i podświetli. L2/R2 i klik prawego touchpada w lizard mode
  klikają myszą, więc je trzeba **przytrzymać ~1 s**.
- Stuknięcie wiersza otwiera wybór akcji: zakładki *Klawiatura* (pełna siatka + F1–F12, nawigacja, numeryczna,
  przełączniki Ctrl/Shift/Alt), *Mysz*, *Specjalne* (klawiatura ekranowa, pauza, edytor, warstwy, profile),
  *Tekst*. Przycisk *Wyczyść* usuwa przypisanie.
- **Warstwy**: przełącznik *Baza / Warstwa: …* nad listą, *+ Warstwa*, *Usuń warstwę*. W warstwie każdy wiersz jest
  opisany jako „z bazy” (szary, dziedziczony) albo „nadpisane w warstwie” (pomarańczowy). *Z bazy* usuwa nadpisanie,
  *Wyczyść* w warstwie wyłącza przycisk tylko w tej warstwie.
- Zakładka **Ustawienia**: tryb gałek (klawisze / mysz / nic), prędkość i krzywa myszy, martwe strefy, próg L2/R2,
  wyłączenie klawiszy lizard mode.
- Pasek na dole pokazuje na żywo, co jest wciśnięte i co by to wysłało (z uwzględnieniem trzymanych warstw).
- **Zapisz** zapisuje plik; jeśli to aktywny profil, od razu przeładowuje go w mapowaniu. Przy zamykaniu lub zmianie
  profilu z niezapisanymi zmianami edytor zapyta, co zrobić.

## Akcje

| Akcja | Działanie |
|---|---|
| `"W"`, `"F1"`, `"Space"`, `"LShift"`, `"Num5"`… | klawisz (wysyłany jako scancode) |
| `"Ctrl+F1"` | kombinacja – wciśnięte i puszczane razem |
| `"LMB"` `"RMB"` `"MMB"` `"Mouse4"` `"Mouse5"` `"WheelUp"` `"WheelDown"` | mysz (w MTA nie działa, patrz niżej) |
| `"Text:/me macha\n"` | wpisuje tekst (`\n` = Enter) |
| `"@Keyboard"` | pokaż/ukryj klawiaturę ekranową |
| `"@Pause"` | wstrzymaj/wznów mapowanie |
| `"@Layer:alt"` | trzymaj, żeby działała warstwa `alt` |
| `"@Profile:MTA"` | przełącz na profil `MTA` |
| `"@NextProfile"` | następny profil (alfabetycznie) |
| `"@Editor"` | otwórz edytor mapowania |
| `""` / `"none"` | nic (w warstwie: wyłącza przypisanie z bazy) |

## Mysz w MTA

MTA ignoruje programową mysz (`SendInput`), za to klawiaturę przyjmuje. Dlatego kamera i strzelanie
zostają na **lizard mode** kontrolera: prawy touchpad to kursor, R2 lewy klik, L2 prawy klik.
`rightStick.mode = "mouse"` i akcje myszy działają na pulpicie i w innych grach, w MTA nie (edytor to podpowiada).
DeckMapper nie wyłącza lizard mode i nie przejmuje na wyłączność urządzeń myszy/klawiatury kontrolera.

## Do sprawdzenia przy pierwszym uruchomieniu

- **Podgląd kontrolera** (menu): naciśnij po kolei przyciski i sprawdź, czy nazwy się zgadzają. Układ bitów
  pochodzi ze sterownika `hid-steam` z Linuksa. Jeśli coś się nie zgadza, popraw bit w `SteamDeckController.Parse`.
- **Lizard mode sam wysyła kilka klawiszy** (np. Enter, Esc, strzałki pod A/B/d-padem). Jeśli się gryzą
  z mapowaniem, włącz w edytorze *Ustawienia → Wyłącz klawisze lizard mode* (`"disableLizardButtons": true`),
  ale sprawdź, czy R2/L2 dalej klikają.
- Logi błędów: `deckmapper.log` obok exe.
