# Frissítés-ötletek (amik tényleg alig léteznek máshol)

## Terminál
1. **Időgép-blokkok** – minden parancs kimenete pillanatképként megmarad; két futás kimenete egymás mellett, színes diff-fel („mi változott a tegnapi `pnpm build` óta?”).
2. **Figyelő-blokk bármilyen parancsra** – egy blokkot „kitűzöl”, és újra lefut, ha a projekt fájljai változnak; a lapon élő ✓/✗ jelvény. Univerzális watch mód, eszközönkénti beállítás nélkül.
3. **Szemétkosár a veszélyes parancsoknak** – `rm -rf`, `Remove-Item -Recurse`, `git clean` helyett a fájlok egy árnyék-kukába kerülnek, 24 óráig egy kattintással visszahozhatók.
4. **Hiba-magyarázó a blokkon** – piros blokk → egy kattintással a hiba + a projekt környezete (package.json, utolsó git diff) megy egy AI-nak, a javaslat futtatható paranccsal tér vissza, amit rákattintva beírsz.
5. **Visszajátszható „run book”** – kijelölsz pár blokkot → exportálható önálló HTML-lejátszóként (parancsok + kimenetek, lépésenként), vagy futtatható scriptként.
6. **Dev-szerver „szellem-panel”** – a hosszan futó szerver kimenete egysoros élő állapotjelzővé zsugorodik a lapsávban (port, utolsó sor, hibaszámláló); kattintásra kinyílik.
7. **Kimenet-térkép** – a görgetősáv mellett minimap: a blokkok színezve (zöld/piros/idő szerint), hibák piros jelekkel; kattintásra ugrik, „csak a hibás blokkokat mutasd” szűrés, kimenet-hajtogatás.
8. **Személyes parancs-előrejelzés** – a saját előzményedből, mappa + git-ág + napszak alapján szürke szellem-szöveg javaslat (helyi modell, felhő nélkül): „ebben a repóban a `git add -A` után mindig `git commit` jön”.
9. **Titok-őr** – API-kulcsot/tokent (minta + entrópia alapján) a kimenetben automatikusan elmos, beillesztés előtt figyelmeztet, a vágólapot 30 mp után törli.
10. **OCR-beillesztés** – Win+Shift+T: kijelölsz a képernyőn bármit (hibaablak, kép, videó), a Windows beépített OCR-je szöveggé alakítja és a terminálba/vágólapra teszi.
11. **Élő előnézet + mobil-teszt** – az előnézeti panelhez hálózat-szimuláció (3G/offline), sötét/világos váltás, eszköz-méretek, és egy QR-kód, amivel a telefonod (ugyanazon a Wi-Fin) azonnal megnyitja a localhost-oldalt (a Vite `--host`-ot a háttérben beállítja).
12. **Hangvezérelt paletta** – „hé Lumen, indítsd a dev szervert / állítsd le a 3000-es portot” – offline beszédfelismeréssel (System.Speech), ugyanazokat az akciókat hívja, mint a parancspaletta.

## Lumen (memória + rendszer)
13. **Projekt-memóriamérleg** – a folyamatokat projektekhez rendeli (munkamappa/parancssor alapján): „climarkai-website: 2,1 GB (node ×7, böngészőfül, Code)”, és egy gombbal „felfüggeszt” (NtSuspendProcess + RAM-ürítés), majd folytat.
14. **Brave/Chrome fül-fagyasztó** – külön-külön látja a füleket (CDP), rangsorolja memória szerint, és bezárás nélkül „lefagyasztja” (`Page.setWebLifecycleState`), kattintásra azonnal visszaébred – a „minden fül fontos” problémára.
15. **Alvó localhost** – a tétlen dev szervereket (10 perce nem jött kérés) automatikusan felfüggeszti, és egy apró helyi proxy az első kérésnél visszaébreszti őket – a memória felszabadul, a munkád nem szakad meg.
16. **Memória-előrejelző** – a saját analitikádból tanult napi mintázat + folyamatonkénti szivárgás-meredekség alapján: „12 perc múlva eléred a 95%-ot, mert a X +40 MB/perc ütemben nő” – és szivárgó programnál „újraindítás munkamenet-visszaállítással” gomb.
17. **Folyamat-boncolás** – bármelyik folyamatra: miért fut (szülő-lánc, melyik autostart / ütemezett feladat / szolgáltatás indította), mit nyitott meg (portok, fájlok, hálózat), és egy kattintásos „indításkor tiltás” – költség-hatással (RAM + bejelentkezési idő).
18. **Lusta autostart** – az induló programokat (nálad ~20 van) nem egyszerre indítja, hanem sorban, az asztal tétlenné válása után 20–30 mp-enként: a bejelentkezés azonnal használható, a háttérprogramok csendben csatlakoznak.
19. **Fókusz-mód** – egy gomb: RAM-seprés, Wallpaper Engine szüneteltetése, dokk elrejtése, értesítések némítása, a nem használt ablakok halványítása; kilépéskor minden visszaáll.
20. **Háttérkép-követő téma** – a Wallpaper Engine élő képéből néhány másodpercenként színpalettát nyer ki, és az üvegfelületek/dokk/terminál kiemelő színe lassan, finoman vele együtt változik.

## Dokk / Spotlight / munkafolyamat
21. **Projekt-munkaterület egy kattintásra** – egy „recept”: szerkesztő megnyitása, terminál-lapok a megfelelő parancsokkal, böngésző a localhost-on, Docker indítás, és az ablakok elrendezése a képernyőn. A dokkról indítható, naponta tanul („hétfő reggel mindig ezt nyitod”).
22. **Munkamenet időgép** – „fagyaszd le ezt az állapotot”: nyitott ablakok helyei, terminál-lapok és parancsok, git stash, böngészőfülek listája; hétfőn egy kattintással visszaáll a péntek 17:00-s helyzet.
23. **Spotlight, ami csinál is** – természetes nyelvű műveletek: „öld meg a 3000-es portot”, „szabadíts fel 2 GB-ot”, „nyisd meg a gamehub projektet és indítsd a dev-et”, „vágólap JSON → TypeScript típusok”.
24. **Vágólap-érzékeny Spotlight** – ha egy stack trace-t, URL-t, színkódot, JSON-t vagy útvonalat másolsz, a Spotlight megnyitáskor a megfelelő műveleteket kínálja (fájl:sor megnyitása szerkesztőben, JSON szépítése, URL előnézete alul, szín-minta).
25. **Élő dokk-jelvények a munkából** – a VS Code ikonon a bukó tesztek száma, a Terminal ikonon a futó blokkok és az előrehaladás-gyűrű, a böngészőn a memóriaköltség – a dokk státusz-műszerfal lesz.
26. **„Mi húzza le a gépet?” mini-riport** – napi/heti automatikus összefoglaló: mi evett a legtöbb RAM-ot, mikor, mi volt a hatékony seprés, mit érdemes bezárni – egy gyönyörű, megosztható kártyán.
