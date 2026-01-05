# LX VPN

Простой и удобный VPN клиент для Windows на базе Xray-core с поддержкой VLESS + REALITY.

## Возможности

- 🔒 Современный протокол VLESS + REALITY
- ⚡ Split Tunneling — только выбранные сайты через VPN
- 🌈 RGB подсветка окна
- 🛡️ Kill Switch — блокировка интернета при обрыве VPN
- 🔐 DNS-over-HTTPS (Cloudflare)
- 📊 Статистика трафика в реальном времени
- 🔄 Автоматические обновления
- 📥 Сворачивание в трей

## Скриншоты

![LX VPN](https://via.placeholder.com/800x500?text=LX+VPN+Interface)

## Требования

- Windows 10/11
- .NET Framework 4.8

## Установка

1. Скачайте последнюю версию из [Releases](https://github.com/iNDiK4/lx-vpn-releases/releases)
2. Запустите `LX VPN.exe`

## Сборка из исходников

```powershell
# Клонировать репозиторий
git clone https://github.com/iNDiK4/lx-vpn-releases.git
cd lx-vpn-releases

# Собрать
msbuild XrayLauncher/XrayLauncher.csproj /p:Configuration=Release
```

## Технологии

- C# / WPF (.NET Framework 4.8)
- [Xray-core](https://github.com/XTLS/Xray-core) — ядро VPN

## Лицензия

MIT License
