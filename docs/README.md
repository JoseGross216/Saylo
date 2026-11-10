# Documentación de SAYLO

## Empleados y supervisores

1. [Manual de uso para empleados](manual-empleados.md): preparar una frase, revisar
   resultados, repetir voz, copiar y terminar una sesión.
2. [Verificación operativa durante una atención](verificacion-operativa.md): lista
   de comprobación antes, durante y después, y comunicación de incidencias a TI.

La versión documentada es un piloto de escritorio. Consultar la disponibilidad
real en el manual: la demostración no realiza inferencia ni genera voz y la captura
nativa aún requiere integración. Estos documentos no certifican un despliegue.

## Pruebas y aceptación

- [Pruebas de escritorio y usabilidad](pruebas-escritorio-usabilidad.md): alcance,
  trazabilidad, ejecución reproducible, resultados y limitaciones.
- [Protocolo de evaluación con empleados](protocolo-usabilidad.md): tareas, métricas
  y ficha pendiente de completar en un piloto con personas.
- Ejecutor: [Test-Desktop.ps1](../tools/Test-Desktop.ps1). Genera TRX y un resumen
  JSON en `artifacts/package6/test-results/`, fuera de Git.

## Desarrollo e integración

- [Interfaz y MVVM](desktop-ui.md).
- [Conversaciones y chat temporal](conversations-chat.md).
- [Traducción escrita y demostración](written-translation-ui.md).
- [Reproducción, estados y errores](playback-states-errors.md).
- [Coordinación de motores](translation-pipeline.md).
- [Instalación y verificación de Argos para TI](local-text-translation.md).
- [Reconocimiento Whisper para TI](whisper-speech-recognition.md).

Para una demostración desde el repositorio, con SDK y dependencias ya disponibles:

```powershell
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj -- --demo
```

La instalación y configuración del puesto corresponden a TI. El empleado utiliza
la aplicación facilitada por la empresa y el manual, sin necesitar comandos.
