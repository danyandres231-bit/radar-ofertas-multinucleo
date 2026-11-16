@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Publicando Radar de Ofertas en Vercel...
call npx vercel link --yes --project radar-ofertas-multinucleo --scope dany24
call npx vercel --prod --yes --scope dany24
echo.
echo Listo. Copia el enlace "Production" de arriba y pasaselo a Claude.
pause
