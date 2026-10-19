# Plan: Sraz ploča (grana `SrazRetro`)

## 0. Pregled

**Cilj igre:** pobjeđuje zadnji igrač koji ima barem jedan pijun na ploči.

**Osnovna pravila**
- Ploča je n×n, zadano 10×10, n mora biti veći od 5.
- Svako polje ima jednu od 8 boja; svaka boja je jedna tema pitanja. Boje se raspoređuju nasumično (ravnomjerno).
- Igra podržava 1 do 4 igrača. Kod 1 igrača protivnik je NPC.
- Svaki igrač dobiva jedan kut i 3 pijuna u L-obliku (kut + 2 susjedna polja).
- Pijun se miče samo vodoravno ili okomito, za 1 polje.
- Svaki pijun smije napraviti jednu akciju po igračevu redu.
- Red završava automatski kad sva 3 pijuna odigraju.

**Ishodi poteza**

| Odredišno polje | Pravilo |
|---|---|
| Prazno | 1 pitanje teme polja. Točan odgovor: pijun se pomiče. Netočan: ostaje. U oba slučaja troši se akcija. |
| Vlastiti pijun | Zabranjeno. Ne troši akciju. |
| Pijun protivnika | Dvoboj: 3 pitanja (2 s ponuđenim odgovorima, 1 s unosom) iz teme polja protivničkog pijuna. Prvi netočan odgovor prekida dvoboj. Ako su sva 3 točna, protivnički pijun se uklanja, a napadač staje na to polje. Troši se akcija. |

## 1. Domain (`src/Sraz.Domain/Models/`)
- `QuestionTheme.cs`: enum od 8 tema (npr. Geography, History, Math, Science, Sport, Art, Literature, Entertainment). Uskladiti s `data/questions.seed.json`.
- `BoardCell.cs`: `Row`, `Column`, `Theme`, `Pawn? Occupant`.
- `Board.cs`: `Size`, `DefaultSize = 10`, za n <= 5 baca `ArgumentOutOfRangeException`; `Cells[,]`, `GetCell`, `GetCornerCells()`, `GetStartingCells(cornerIndex)`, `GetNeighbor(row, col, direction)` (null izvan ploče).
- `Direction.cs`: `Up, Down, Left, Right`.
- `Pawn.cs`: `Id`, `OwnerIndex`, `Row`, `Column`, `HasActedThisTurn`, `IsCaptured`.
- `Player.cs`: `Name`, `Index` (0-3), `Color`, `IsNpc`, `Pawns`, izračunato `IsEliminated`.
- `MoveOutcome.cs`: `Moved, Stayed, Captured, Blocked`.
- `GameState.cs`: `InProgress`, `Finished`, uz `Winner`.
- Format pitanja (`MultipleChoice` i `FreeText`): provjeriti `Question.cs` i `QuestionType.cs`, proširiti ako treba.

## 2. Application
- `IBoardFactory` / `BoardFactory`: `Create(int size = 10, int? seed = null)`; ravnomjerna nasumična raspodjela tema (na 10×10 po 12-13 polja po temi); seed za ponovljive testove.
- `GameEngine` / `IGameEngine`:
  - `StartGame(playerNames, boardSize = 10)`: 1 ljudski igrač + NPC u suprotnom kutu; 2-4 igrača po kutovima (2 dijagonalno); više od 4 baca iznimku.
  - `CurrentPlayer`, `BeginTurn()`, `EndTurn()`, `NextTurn()` (preskače eliminirane).
  - Automatski kraj reda kad svi aktivni pijuni igrača odigraju.
  - `GetAvailableMoves(pawn)`: prazno ako je pijun već igrao; isključuje vlastite pijune i polja izvan ploče; uključuje protivničke pijune kao napad.
  - `RequestMove(pawn, direction)`: jedno pitanje ili `Duel` s 3 pitanja.
  - `ResolveMove(pawn, direction, isCorrect)` i `ResolveDuel(pawn, direction, results)`; akcija se troši u svim ishodima osim `Blocked`.
  - Dvoboj: 2 pitanja s izborom + 1 s unosom, bez ponavljanja, iz teme polja protivničkog pijuna; prekid na prvom netočnom.
  - Provjera unosa: bez razlike u velikim/malim slovima, trim razmaka, lista `acceptedAnswers`.
  - `CheckGameEnd()` nakon svakog `Captured`; kad ostane jedan igrač s pijunima: `Finished`, `Winner`. Nakon kraja `RequestMove`/`ResolveMove` bacaju `InvalidOperationException`.
  - Mapiranje `QuestionTheme` na `Category` preko `IQuestionProvider`.
- NPC: `INpcStrategy` / `RandomNpcStrategy`: nasumičan pijun koji još nije igrao i nasumičan valjan smjer; nikad ne cilja vlastiti pijun; točnost odgovora je postavka (zadano ~60%), u dvoboju se primjenjuje na svako od 3 pitanja; opcionalni seed.

## 3. Podaci (`data/questions.seed.json`)
- Za svaku od 8 tema dodati pitanja s unosom i dovoljno pitanja s izborom (min. 2 izbor + 1 unos po temi; preporuka 3-5 svake vrste).
- Pitanja s unosom imaju `acceptedAnswers`.
- Uklopiti se u postojeći format (`QuestionRecord.cs`, `Question.cs`, `JsonQuestionProvider.cs`).

## 4. UI (`src/Sraz.UI.Wpf/`)
- `ThemeColors.cs`: tema -> boja + lokalizirani naziv.
- `BoardWindow`: `UniformGrid` n×n s obojenim poljima, legenda tema, tooltip s temom; pijuni kao slika šahovskog pijuna u boji igrača; oznaka čiji je red, brojač "Preostalo akcija: X/3"; odigrani pijuni zatamnjeni; nema gumba "Završi potez".
- Kretanje: klik na vlastiti pijun naglašava dopuštena polja; protivnički pijuni označeni kao napad; klik na polje otvara `QuestionWindow` ili `DuelWindow`.
- `DuelWindow`: 3 pitanja (2 izbor, 1 unos), napredak "Pitanje 1/3", prekid na prvom netočnom.
- NPC potezi automatski s kratkom pauzom i porukom.
- Ekran kraja igre: pobjednik (ili "NPC je pobijedio"), "Nova igra", "Povratak na izbornik".
- Obavijest o eliminaciji igrača i uklonjenom pijunu.
- `ViewModels/BoardViewModel.cs` (nasljeđuje `BindableBase`).
- `MainWindow.xaml`: broj igrača (1-4), imena, veličina n, gumb "Ploča"; poruka greške za n <= 5.
- `Localizer.cs`: novi tekstovi (HR/EN).

## 5. Testovi (`tests/Sraz.Tests/`)
- Ploča: 10×10 zadano; n <= 5 iznimka; 6, 8, 15 ispravno; sve teme prisutne i ravnomjerne; isti seed = isti raspored; svaka tema ima boju.
- Postavljanje: 3 pijuna u L-obliku po igraču za 2, 3, 4 igrača na pločama 6, 10, 15; bez preklapanja; 5+ igrača iznimka; 1 igrač = NPC u suprotnom kutu.
- Potezi: samo gore/dolje/lijevo/desno po 1 polje; točan pomiče, netočan ostavlja; jedna akcija po pijunu po redu (i netočan je troši); reset u novom redu; tema pitanja = tema odredišta.
- Auto-kraj reda: nakon 3. pijuna prelazi, nakon 1. i 2. ne; nakon zadnjeg igrača vraća na prvog; reset `HasActedThisTurn`.
- Zauzeta polja i dvoboj: vlastito polje nije dopušteno i ne troši akciju; protivnik pokreće dvoboj (2 izbor + 1 unos) iz teme njegova polja; 3 točna = uklonjen i napadač na polju; netočan prekida i troši akciju; provjera unosa; auto-kraj reda nakon dvoboja.
- Kraj igre: uklanjanje zadnjeg pijuna završava igru; u 3-4 igrača eliminacija jednog ne završava; eliminirani se preskače; nakon kraja potezi nedopušteni; 1 igrač vs NPC u oba smjera.
- NPC: samo valjani potezi, ne cilja vlastiti pijun, poštuje jednu akciju po pijunu.
- Seed pitanja: svaka tema ima min. 2 izbor + 1 unos.

## 6. Redoslijed implementacije
1. Domain modeli.
2. `BoardFactory` i testovi ploče.
3. Seed pitanja i proširenje `Question`/`QuestionRecord`.
4. `GameEngine`: postavljanje, redoslijed, potezi.
5. Zauzeta polja, dvoboj, provjera unosa, kraj igre.
6. NPC strategija.
7. UI: `BoardWindow`, `DuelWindow`, `BoardViewModel`, `MainWindow`, lokalizacija.
8. Završni testovi i provjera.

## 7. Izvan opsega za sada
- Dodatna pravila i složeniji ciljevi.
- Napredna težina NPC-a.
- Mrežna igra.
