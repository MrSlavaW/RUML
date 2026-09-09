# RUML Translations Repository

Официальный репозиторий сообщества для хранения и распространения переводов модов RimWorld через мод **RUML** (Universal Localization).

## Структура репозитория

Переводы хранятся в открытых XML-папках:
```text
Translations/
├── [НазваниеМода]/
│   └── [Язык (например Russian-SK или Russian)]/
│       └── [ИмяАвтора]/
│           ├── info.json          <-- Метаданные (packageId, версия, описание)
│           ├── DefInjected/       <-- Папки с дефами
│           │   ├── ThingDef/
│           │   └── ...
│           └── Keyed/             <-- Папка со строками интерфейса
│               └── Keys.xml
```

## Как добавить свой перевод

1. Создайте в папке `Translations/` каталог вашего мода: `Translations/<ИмяМода>/<Язык>/<ВашНик>/`
2. Положите туда файл `info.json` следующего формата:
```json
{
  "packageId": "author.modpackageid",
  "version": "1.0.0",
  "description": "Описание перевода"
}
```
3. Положите ваши папки `DefInjected/` и/или `Keyed/`.
4. Отправьте Pull Request в ветку `main`.

После слияния GitHub Actions автоматически упакует архив в папку `packs/` и обновит файл `manifest.json`.
Игроки увидят ваш перевод в игре в меню мода RUML!
