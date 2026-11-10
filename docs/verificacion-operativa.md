# Verificación operativa durante una atención

Piloto · 29 de octubre de 2026 · Para empleado, supervisor y TI.

Usar junto al [manual para empleados](manual-empleados.md). Esta lista verifica
el puesto concreto; la aprobación técnica del desarrollo no equivale a autorizar
una operación empresarial. La demostración solo acredita práctica con ejemplos.

## Antes de atender

- [ ] TI identificó la versión y el sistema del puesto y confirmó sus funciones.
- [ ] La cabecera muestra el modo esperado; Demostración no se usa como traductor real.
- [ ] Una frase ficticia funciona de español a inglés y otra de inglés a español.
- [ ] El original se conserva y la traducción corresponde al idioma de destino.
- [ ] Si se necesita pronunciación, hay una guía correcta para la variante acordada.
- [ ] Si se necesita voz, la frase escrita genera audio y se oye una sola vez sin
  pulsar Repetir; Detener y Repetir funcionan en la salida prevista.
- [ ] Si se necesita voz de entrada, el micrófono autorizado graba, transcribe,
  permite corregir, añadir al borrador y traducir; la voz resultante también empieza sola.
- [ ] Copiar traducción produce el texto esperado en un destino de prueba autorizado.
- [ ] Al cerrar y volver a abrir no aparecen mensajes ni borradores de la prueba.
- [ ] Se conoce la vía alternativa de atención y el contacto de soporte.

**Decisión del supervisor/TI:** marcar «texto», «texto y voz» o «no habilitado».
Una función requerida que esté pendiente o falle impide aprobar ese recorrido.
En la composición actual, captura, pronunciación y síntesis reales están pendientes:
el puesto no se declara aprobado para el recorrido completo con voz.

## Durante cada atención

1. Comprueba origen y destino antes de cada frase; intercambiarlos conserva el borrador.
2. Revisa la transcripción antes de añadirla y enviar. El borrador anterior también se envía.
3. Comprueba la traducción en el chat antes de leerla o copiarla. No esperes a la voz
   para acceder al texto si ya está disponible.
4. Verifica si la reproducción automática está activada. La voz es local y no se
   inyecta en la llamada; comunícate por la herramienta de atención habitual.
5. Si aparece un aviso, utiliza la recuperación del manual. Ante un fallo de voz,
   conserva la traducción y continúa por texto si ese recorrido está autorizado.
6. Si la detención no se confirma, usa Reintentar detención; no inicies otra grabación.

## Al finalizar la atención

- [ ] Se detuvo cualquier reproducción o grabación activa.
- [ ] Se limpió el chat con confirmación cuando ya no era necesario.
- [ ] Se borró también el borrador si no corresponde a la siguiente atención.
- [ ] Se comprobó el destino antes de pegar contenido copiado.
- [ ] Al terminar el turno se cerró SAYLO. El contenido pegado y el portapapeles
  se gestionan mediante el procedimiento de la empresa, fuera del chat temporal.

## Verificación técnica antes del piloto

Responsable: TI, con apoyo del equipo técnico. Registrar resultado y evidencia
por cada sistema y puesto; no rellenar casillas como aprobadas sin ejecutarlas.

| Comprobación | Criterio | Resultado por completar |
| --- | --- | --- |
| Instalación e integridad | Programa, versiones y modelos aprobados verificables en el puesto. | Pendiente |
| Texto real ES→EN / EN→ES | Frases empresariales fuera del catálogo demo, significado e idioma correctos. | Pendiente |
| Pronunciación | Guía del idioma/variante de destino, identificada como aproximada o IPA. | Pendiente |
| Voz desde texto y grabación | TTS termina y autoplay se inicia una sola vez en ambos modos; repetir no retraduce. | Pendiente con motores y dispositivos reales |
| Captura y permisos | Solo micrófono seleccionado, inicio voluntario y máximo 30 s; rechazo recuperable. | Pendiente de adaptador nativo |
| Funcionamiento sin internet | Con instalación completa y equipo desconectado, todos los recorridos requeridos funcionan. | Pendiente; no equivale a usar dobles de prueba |
| Independencia de telefonía | Sin conexión de SAYLO a la herramienta de llamada y sin lectura de su audio. | Pendiente en la estación real |
| Latencia y consumo | Medir según los objetivos del SRS sobre el hardware del piloto. | Pendiente; las pruebas con dobles no miden inferencia |
| Teclado y ayudas técnicas | Completar las tareas con teclado y el lector de pantalla del puesto, cuando corresponda. | Pendiente de evaluación con empleados |

Guías para TI: [Argos](local-text-translation.md),
[Whisper](whisper-speech-recognition.md) y
[protocolo de usabilidad](protocolo-usabilidad.md).

## Ficha de incidencia

Copiar esta ficha en el canal interno autorizado de soporte y completarla sin
adjuntar conversaciones de clientes:

```text
Fecha y hora local:
Puesto / versión de SAYLO / sistema operativo:
Modo mostrado en la cabecera:
Recorrido: texto / micrófono / traducción / pronunciación / voz / copia / limpieza
Idiomas de origen y destino:
Pasos con una frase ficticia:
Resultado esperado:
Aviso visible o resultado observado:
¿Se conserva el texto?:
¿Permite reintentar? ¿Qué ocurrió?:
¿Se pudo continuar por el recorrido alternativo autorizado?:
Responsable y referencia de soporte:
```

No se envía esta ficha automáticamente: el empleado decide qué comunicar por el
canal establecido. No incluir grabaciones, transcripciones ni identificadores de clientes.
