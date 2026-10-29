# Base de interfaz de SAYLO — Paquete 1

La ventana principal utiliza Avalonia 12, CommunityToolkit.Mvvm y bindings
compilados. Se mantiene `net8.0`, como el resto de la solución. El arranque
existente en `App.axaml.cs` compone `MainViewModel` y asigna su `DataContext`.

## Organización

- `MainViewModel`: compone el espacio de trabajo y sus ViewModels hijos.
- `LanguageSelectionView` / `LanguageSelectionViewModel`: selectores manuales
  español e inglés, intercambio y dirección visible. Elegir el idioma del otro
  selector intercambia la pareja. Nunca se notifica una pareja con idiomas iguales.
- `TextInputView` / `TextInputViewModel`: editor multilínea, contador y borrado.
  El borrador permanece en memoria y se conserva al cambiar los idiomas.
- `LanguageOption`: etiquetas de UI para los tipos `Language` del contrato público.
- `Styles/SayloTheme.axaml`: colores, tipografía, superficies, controles e iconos
  vectoriales compartidos. Tema claro explícito para mantener el contraste.

Las vistas solamente inicializan XAML; las reglas de selección y entrada residen
en ViewModels sin controles ni tipos de motores. Desktop referencia únicamente
Translation.Contracts; no necesita arrancar Api, Identity ni un servidor.

## Alcance y punto de integración

Este paquete construye la base de la ventana. Traducir y grabar se muestran
deshabilitados con una explicación de disponibilidad. No se generan resultados
de ejemplo ni se accede al micrófono. La conversación presenta su estado vacío
y anticipa texto, pronunciación aproximada y voz automática según SRS v0.6.

`TextInputViewModel.CreateRequest(source, target)` prepara un
`TextTranslationRequest` validado para la integración posterior del comando de
traducción. Rechaza entradas vacías o de espacios, preserva el original y usa
los mismos idiomas del contrato. Los 160 caracteres del SRS son una condición
de benchmark, no un límite de entrada.

El envío, las actualizaciones progresivas del resultado, la conversación,
STT/TTS, la reproducción y la comprobación de modelos corresponden a los
siguientes paquetes. La reproducción deberá ocurrir una sola vez por operación
vigente, conservando el texto ante errores de pronunciación o voz.

## Uso y accesibilidad

- Selectores y editor con etiquetas y nombres para tecnologías de asistencia.
- Navegación con Tab y foco visible de Fluent; Alt+Abajo abre los selectores.
- `Ctrl+Mayús+S`: intercambiar idiomas.
- `Ctrl+L` dentro del editor: borrar el borrador; también existe un botón.
- `Enter`: nueva línea. Los atajos habituales de copiar/pegar siguen disponibles.
- Ventana inicial de 1080 × 820, mínimo de 760 × 740 unidades lógicas. El área
  central se desplaza verticalmente cuando falta altura; el editor desplaza
  internamente el texto largo.

## Validación

```powershell
dotnet build Saylo.slnx -c Release
dotnet test Saylo.slnx -c Release
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj
```

`Saylo.Desktop.Tests` verifica cambios de idioma sin estados inválidos, borrador
conservado, validación de entradas, dirección del contrato y borrado de espacios.
La comprobación visual se realiza en Windows; macOS y Linux requieren su propia
validación antes de declarar compatibilidad.
