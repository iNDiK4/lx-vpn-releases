# LX VPN Releases

Этот репозиторий содержит только информацию об обновлениях и релизы.

## Текущая версия: 2.0.0

### Как обновить version.json

При выпуске новой версии:
1. Измените `version` на новую версию (например, "2.1.0")
2. Обновите `changelog` с описанием изменений
3. Загрузите новый .exe в GitHub Releases
4. Обновите `downloadUrl` на ссылку нового релиза
5. Сделайте commit и push

### Формат version.json

```json
{
  "version": "2.1.0",
  "changelog": "• Новая функция\n• Исправление бага",
  "downloadUrl": "https://github.com/USERNAME/REPO/releases/download/v2.1.0/LX_VPN.exe"
}
```
