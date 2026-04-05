#!/bin/sh
# .NET API arka planda port 5000'de başlar (nginx container içinden proxy'ler)
ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet /app/api/HemenIlanVer.Api.dll &

# nginx ön planda 8080'de çalışır (Railway bu portu dışarı açar)
nginx -g 'daemon off;'
