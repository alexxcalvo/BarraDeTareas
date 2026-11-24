# Taskbar Desk Controller

Pequeña aplicación para Windows 11 que cambia el auto-ocultamiento de la barra de tareas según la ventana activa. Usa el estado nativo de Windows, por lo que conserva el comportamiento de la barra, el hover en el borde y los atajos `Win+1` y `Win+2`.

## Descargar y ejecutar

1. Descarga el [ZIP del repositorio](https://github.com/alexxcalvo/BarraDeTareas/archive/refs/heads/master.zip) o el [ejecutable](https://github.com/alexxcalvo/BarraDeTareas/blob/master/TaskbarDeskController.exe?raw=true).
2. Si descargaste el ZIP, extráelo.
3. Ejecuta `TaskbarDeskController.exe`. La aplicación queda en el área de notificación, junto al reloj.

Requisitos: Windows 11 y .NET Framework 4.8 o posterior. Windows 11 incluye .NET Framework 4.8 o 4.8.1. No necesita permisos de administrador ni instala servicios.

> El ejecutable incluido no está firmado digitalmente. Si Windows muestra SmartScreen, revisa el código fuente y compílalo tú mismo si prefieres no ejecutar un binario sin firma.

## Qué hace

- En el escritorio, activa el auto-ocultamiento nativo de la barra de tareas.
- Con una ventana normal en primer plano, desactiva el auto-ocultamiento.
- En una ventana a pantalla completa, conserva el estado actual sin cambiarlo.
- Al salir, intenta restaurar el estado de auto-ocultamiento que tenía Windows cuando se inició la aplicación.
- Si el proceso se interrumpe mientras modificó ese estado, guarda una copia local para restaurarla en el próximo inicio.

No oculta directamente la ventana de la barra de tareas: usa la API nativa de AppBar de Windows.

## Uso

- **Doble clic** en el icono de la bandeja: activa o pausa el comportamiento automático.
- **Clic derecho** en el icono: abre el menú.
- **Iniciar con Windows**: activa o desactiva el inicio automático para el usuario actual. Está desactivado inicialmente y no requiere permisos de administrador.
- **Salir**: cierra la aplicación e intenta restaurar el estado original de la barra.

## Compilar desde el código fuente

Abre `TaskbarDeskController.csproj` en Visual Studio o usa una consola de desarrollador con MSBuild instalado y el paquete de desarrollador de .NET Framework 4.8. El proyecto no necesita paquetes NuGet.

```powershell
msbuild TaskbarDeskController.csproj /p:Configuration=Release
```

El ejecutable se genera en `bin\Release\TaskbarDeskController.exe`.

- [Instalación de .NET Framework](https://learn.microsoft.com/dotnet/framework/install/)
- [Paquete de desarrollador de .NET Framework](https://learn.microsoft.com/dotnet/framework/install/guide-for-developers)

## Datos locales y permisos

La aplicación no realiza conexiones de red ni envía datos. Guarda su preferencia de activación y el estado temporal de recuperación en `%LOCALAPPDATA%\TaskbarDeskController`. La opción de inicio automático añade una entrada bajo `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, solo para la cuenta actual.

## Desinstalar

1. Si el inicio automático está activado, abre el menú y desactívalo.
2. Elige **Salir** para que la aplicación intente restaurar el estado original de la barra.
3. Borra `TaskbarDeskController.exe` o la carpeta extraída.
4. Opcionalmente, borra `%LOCALAPPDATA%\TaskbarDeskController` para eliminar las preferencias locales.

## Licencia

Este proyecto se distribuye bajo la licencia MIT. Consulta [LICENSE](LICENSE).
