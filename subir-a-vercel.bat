@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Publicando Radar de Ofertas en Vercel...
call npx vercel --prod --yes
echo.
echo Listo. Copia el enlace "Production" de arriba y pasaselo a Claude.
pause
