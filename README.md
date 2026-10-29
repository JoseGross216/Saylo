# SAYLO

Aplicación de escritorio para preparar mensajes entre español e inglés durante
una atención. Utiliza Avalonia y MVVM sobre .NET 8, con procesamiento local tras
contratos de motores. La conversación es temporal y el audio se reproduce en el
equipo; no se conecta a herramientas de llamadas.

## Documentación

- [Manual para empleados](docs/manual-empleados.md).
- [Verificación operativa durante una atención](docs/verificacion-operativa.md).
- [Pruebas funcionales y usabilidad](docs/pruebas-escritorio-usabilidad.md).
- [Índice completo de documentación](docs/README.md).

Estado: piloto. La traducción real requiere configuración de Argos y sus modelos;
la captura nativa, pronunciación y síntesis reales todavía requieren integración.
El modo de demostración solo utiliza frases precargadas y no genera audio.

## Demostración y pruebas

Con SDK y dependencias disponibles, desde la raíz:

```powershell
dotnet run --project src/Saylo.Desktop/Saylo.Desktop.csproj -- --demo
./tools/Test-Desktop.ps1
```

El ejecutor conserva TRX y un resumen JSON en `artifacts/package6/test-results/`.
Para las comprobaciones Windows de salida con PCM silencioso, añadir `-NativeAudio`.
La evaluación con empleados y la validación del puesto real se describen en el
[protocolo de usabilidad](docs/protocolo-usabilidad.md).
