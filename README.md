# DeckMapper

Mapuje wbudowany kontroler Steam Decka (Windows) na klawiaturę i mysz. Działa bez Steama, z zasobnika.
Ma własną klawiaturę ekranową, która nie zabiera grze fokusu.

## Budowanie (Mac, Rider albo terminal)

```bash
dotnet publish -c Release
```

Wynik: `bin/Release/net8.0-windows/win-x64/publish/DeckMapper.exe`, jeden plik, bez instalowania .NET na Decku.
Na macOS się nie uruchomi (to WinForms), ale zbuduje się dzięki `EnableWindowsTargeting`.

Testy logiki (mapa lizarda, filtr, wczytywanie profili) działają także na macOS:

```bash
cd tests/DeckMapper.Tests && dotnet test
```

## Uruchomienie na Decku

1. Skopiuj `DeckMapper.exe` do jakiegoś folderu (np. `C:\Tools\DeckMapper`).
2. **Zamknij Steama całkowicie.** Gdy Steam działa, przejmuje kontroler i wyłącza lizard mode.
3. Odpal `DeckMapper.exe`. Obok utworzy się folder `profiles/` z profilem `MTA.json` (domyślne mapowanie pod MTA/GTA SA)
   i plik `settings.json`. Jeśli obok leżał stary `mapping.json`, zostanie przeniesiony do `profiles/MTA.json`
   (oryginał zostaje jako `mapping.json.bak`).
4. Przy pierwszym uruchomieniu DeckMapper zaproponuje **Wykryj mapę lizarda** – warto, patrz „Mysz i lizard mode”.
5. W MTA ustaw tryb wyświetlania na **okno bez ramki (borderless)**, inaczej klawiatura ekranowa nie będzie widoczna nad grą.

Autostart: skrót do exe w `shell:startup` (Win+R).

## Ikonka w zasobniku

- **Lewy klik**: klawiatura ekranowa
- **Prawy klik**: menu: profil (podmenu z listą, następny profil, automatyczne przełączanie), edytor mapowania,
  klawiatura, wstrzymaj, **Filtr lizarda: włączony/wyłączony** (przełącznik jednym klikiem), **Wykryj mapę lizarda…**,
  podgląd kontrolera, edycja pliku profilu w Notatniku, folder profili, przeładuj, zamknij
- Tooltip ikonki pokazuje profil, stan kontrolera i filtra lizarda.

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
- Przy kontrolkach, dla których lizard coś robi, jest **plakietka**: „lizard: Enter” (przekreślona = filtr ją połyka,
  pomarańczowa = Enter zadziała razem z Twoją akcją) albo zielona z kłódką „sprzętowy klik LMB” (działa zawsze, także
  w MTA – możesz dodać własny klawisz, ale klik zostanie). Wybór akcji dla takiej kontrolki też o tym przypomina.
- Stuknięcie wiersza otwiera wybór akcji: zakładki *Klawiatura* (pełna siatka + F1–F12, nawigacja, numeryczna,
  przełączniki Ctrl/Shift/Alt), *Mysz*, *Specjalne* (klawiatura ekranowa, pauza, edytor, warstwy, profile),
  *Tekst*. Przycisk *Wyczyść* usuwa przypisanie.
- **Warstwy**: przełącznik *Baza / Warstwa: …* nad listą, *+ Warstwa*, *Usuń warstwę*. W warstwie każdy wiersz jest
  opisany jako „z bazy” (szary, dziedziczony) albo „nadpisane w warstwie” (pomarańczowy). *Z bazy* usuwa nadpisanie,
  *Wyczyść* w warstwie wyłącza przycisk tylko w tej warstwie.
- Zakładka **Ustawienia**: tryb gałek (klawisze / mysz / nic), prędkość i krzywa myszy, martwe strefy, próg L2/R2,
  sekcja **Mysz i lizard** (filtr klawiszy lizarda, lista połykanych klawiszy, kreator mapy).
- Pasek na dole pokazuje na żywo, co jest wciśnięte i co by to wysłało (z uwzględnieniem trzymanych warstw).
- **Zapisz** zapisuje plik; jeśli to aktywny profil, od razu przeładowuje go w mapowaniu. Przy zamykaniu lub zmianie
  profilu z niezapisanymi zmianami edytor zapyta, co zrobić.

## Akcje

| Akcja | Działanie |
|---|---|
| `"W"`, `"F1"`, `"Space"`, `"LShift"`, `"Num5"`… | klawisz (wysyłany jako scancode) |
| `"Ctrl+F1"` | kombinacja – wciśnięte i puszczane razem |
| `"LMB"` `"RMB"` `"MMB"` `"Mouse4"` `"Mouse5"` `"WheelUp"` `"WheelDown"` | mysz (w MTA na serwerze nie działa, patrz niżej) |
| `"Text:/me macha\n"` | wpisuje tekst (`\n` = Enter) |
| `"@Keyboard"` | pokaż/ukryj klawiaturę ekranową |
| `"@Pause"` | wstrzymaj/wznów mapowanie |
| `"@Layer:alt"` | trzymaj, żeby działała warstwa `alt` |
| `"@Profile:MTA"` | przełącz na profil `MTA` |
| `"@NextProfile"` | następny profil (alfabetycznie) |
| `"@Editor"` | otwórz edytor mapowania |
| `"@ProfileWheel"` | trzymaj = koło profili, puść = wybór (patrz „Koło profili”) |
| `""` / `"none"` | nic (w warstwie: wyłącza przypisanie z bazy) |

## Koło profili

Szybka zmiana profilu jak radial menu w Steam Input.

1. W edytorze przypisz do dowolnego przycisku akcję **Specjalne → Koło profili** (`"@ProfileWheel"`), np. trzymane View.
2. W zakładce **Koło** ustaw 2–8 slotów: stuknij slot (w podglądzie albo na liście) i wybierz profil, *↺ / ↻* zmienia
   kolejność, *−/+* liczbę slotów. Koło startuje puste. Sloty są wspólne dla wszystkich profili i zapisują się od razu
   w `settings.json` (`"wheel": { "slots": ["MTA", null, …] }`). Zmiana nazwy / usunięcie profilu aktualizuje sloty.
3. Per profil: **etykieta** w kole (`"wheelLabel"`, maks. 6 znaków – licznik w edytorze; bez niej pierwsze 6 znaków
   nazwy) i **ikona** (`"wheelIcon"`: emoji albo plik PNG z `profiles/icons/`). Te pola zapisują się z profilem.

W grze: **trzymaj** przycisk – na środku ekranu pojawia się koło (aktywny profil ma zieloną obwódkę). Wybierz slot
**prawą gałką** albo **palcem na lewym touchpadzie** i **puść** przycisk. Puszczenie z gałką/palcem w środku albo
na pustym slocie = anuluj. Po wyborze na ~1 s pokazuje się nazwa profilu. Gdy koło jest otwarte, mapowanie jest
wstrzymane, a trzymane klawisze puszczone (filtr lizarda działa dalej). Koło nie zabiera grze fokusu – gra musi być
w trybie okna bez ramki, tak jak dla klawiatury ekranowej. W edytorze podgląd koła reaguje na żywo na gałkę i touchpad.

## Mysz i lizard mode

**Dlaczego mysz zostaje sprzętowa.** MTA po połączeniu z serwerem (już w panelu logowania) przyjmuje tylko „prawdziwą”
mysz. Ruch i kliki wysyłane programowo (`SendInput` – akcje `LMB`/`RMB`, `rightStick.mode = "mouse"`) są tam ignorowane,
tak samo jak mysz ze Steam Input. Działa za to mysz z **lizard mode** kontrolera: prawy touchpad = kursor i kamera,
R2 = lewy klik, L2 = prawy klik, klik prawego touchpada = lewy klik. DeckMapper zostawia więc lizard mode włączony
w całości i przywraca jego domyślne ustawienia przy starcie, zmianie trybu i zamknięciu.
(Na pulpicie i w menu głównym MTA programowa mysz działa normalnie.)

**Filtr klawiszy lizarda.** Lizard mode wysyła też klawisze: np. Enter pod A, Esc pod B, strzałki pod d-padem.
Filtr (globalny hook klawiatury Windowsa) połyka te klawisze, zanim dotrą do gry – ale tylko te, które przychodzą
ze sprzętu. Twoje mapowanie i klawiatura ekranowa DeckMappera wysyłają klawisze programowo, więc filtr ich nie rusza.
Myszy filtr nie dotyka. Działa także w MTA na serwerze.

- **Per profil** (edytor → Ustawienia → *Mysz i lizard*):
  - *Filtruj klawisze lizarda (zalecane)* – mysz z touchpada działa, A/B/d-pad nie wysyłają Enter/Esc/strzałek,
    wszystko mapujesz sam;
  - *Lizard bez filtra* – przyciski dodatkowo wysyłają swoje klawisze;
  - lista połykanych klawiszy: *Wszystkie z mapy (auto)* albo wybrane.
  W pliku: `"lizard": { "filterKeys": true, "swallow": "auto" }` albo `"swallow": ["Enter", "Esc"]`.
  Profil bez tej sekcji działa jak `filterKeys: true, swallow: "auto"`. Stara opcja `disableLizardButtons`
  jest ignorowana (na Decku wyłączała też mysz) i znika przy zapisie profilu.
- **Mapa lizarda** (`lizard-map.json` obok exe) mówi, co lizard robi dla każdego przycisku. Tworzy ją kreator
  **Wykryj mapę lizarda** (menu ikonki albo edytor): wciskasz po kolei przyciski Decka, każdy możesz pominąć,
  na końcu jest podsumowanie do zapisania. Przyciski kreatora stukasz palcem – sprzętowe klawisze i kliki są na czas
  kreatora przechwytywane. Bez mapy działa wbudowana, **niezweryfikowana** lista (Enter, Esc, strzałki).
- **Klawiatura USB / Bluetooth:** filtr połyka też JEJ Enter/Esc/strzałki (nie da się odróżnić, z której klawiatury
  przyszedł sprzętowy klawisz). Gdy DeckMapper wykryje taką klawiaturę, pokaże raz dymek. Wtedy wyłącz filtr w menu
  ikonki (*Filtr lizarda*) – to globalny wyłącznik (`settings.json`: `"lizardFilterEnabled": false`).
- Log (`deckmapper.log`): włączenie/wyłączenie filtra z powodem, założenie/zdjęcie hooka, liczba połkniętych zdarzeń
  co minutę, komendy do kontrolera z wynikiem.

## Do sprawdzenia przy pierwszym uruchomieniu

- **Podgląd kontrolera** (menu): naciśnij po kolei przyciski i sprawdź, czy nazwy się zgadzają. Układ bitów
  pochodzi ze sterownika `hid-steam` z Linuksa. Jeśli coś się nie zgadza, popraw bit w `SteamDeckController.Parse`.
- **Wykryj mapę lizarda** (menu ikonki), żeby filtr znał dokładną listę klawiszy lizarda Twojego Decka.
- Logi błędów: `deckmapper.log` obok exe.
