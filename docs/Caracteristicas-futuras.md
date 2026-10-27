# Características futuras de MegaCopier

Fecha: **26 de octubre de 2026**. Estado: **criterios y orden consensuados entre Claude y Codex; propuestas pendientes de validación e implementación**.

Este documento conserva **las 27 propuestas originales: 20 de Claude (C01–C20) y 7 de Codex/Astra (A01–A07)**. Ninguna se elimina. Los identificadores corresponden al orden de las listas originales; el número de posición corresponde a la prioridad acordada para evaluarlas e invertir en ellas.

La visión común es que MegaCopier permita trasladar trabajo completo, entender lo que ocurre y comprobar qué se puede recuperar. No se presupone que estas funciones estén implementadas ni que sean invenciones inéditas. Las funciones actuales se describen en [Características](Caracteristicas.md); estas propuestas son ampliaciones o nuevos flujos sobre esa base.

## Cómo leer la prioridad y la utilidad

- **P1:** primera prioridad por confianza, alcance o utilidad cotidiana.
- **P2:** siguiente prioridad para necesidades concretas de alto valor. Puede exigir un prototipo antes de construir.
- **P3:** validar demanda, precisión o viabilidad antes de comprometer desarrollo amplio.
- **P4:** conservar para un público especializado o un beneficio todavía no demostrado.
- **Utilidad:** valoración razonada para el público indicado, no resultado de entrevistas ni estadística de uso.
- **Quién probablemente no la necesita:** situaciones con poco beneficio o donde la función no es apropiada; no excluye de forma absoluta a una profesión o tipo de persona.
- **Esfuerzo:** estimación cualitativa relativa, no presupuesto ni plazo. Integración, accesibilidad, mantenimiento y pruebas también cuestan.
- **Orden de inversión y orden de construcción:** se relacionan, pero no son lo mismo. Una base técnica puede construirse antes de su pantalla; un prototipo de P3 puede ser breve y comenzar antes de terminar todo P1. No hay calendario comprometido.

Las 27 entradas permanecen separadas para mantener trazabilidad. Las familias compartidas se diseñan con componentes comunes: **27 entradas no significan 27 motores independientes ni 27 nuevas opciones obligatorias en la interfaz**.

## Lista completa por prioridad

| Orden | ID | Característica | Prioridad | Utilidad prevista | Siguiente paso |
|---|---|---|---|---|---|
| 1 | C12 | [Prevención de errores peligrosos antes de copiar](#c12) | P1 | Alta y transversal | Diseñar una primera versión acotada |
| 2 | C11 | [Diagnóstico «¿Por qué va lento?»](#c11) | P1 | Alta y transversal | Diseñar una primera versión acotada |
| 3 | C18 | [Estimación de duración basada en historial y tipo de archivos](#c18) | P1 | Alta en transferencias largas | Diseñar una primera versión acotada |
| 4 | C15 | [Catálogo de discos y búsqueda incluso desconectados](#c15) | P1 | Alta para colecciones repartidas | Diseñar una primera versión acotada |
| 5 | A01 | [Mapa de protección y revisión antes de liberar espacio](#a01) | P1 | Alta para datos importantes en varios destinos | Diseñar una primera versión acotada |
| 6 | C05 | [Verificador y reparador portátil junto a la copia](#c05) | P1 | Alta para respaldo y archivo | Diseñar una primera versión acotada |
| 7 | A03 | [Ensayos de recuperación de las copias de seguridad](#a03) | P1 | Alta para respaldo fiable | Diseñar una primera versión acotada |
| 8 | C06 | [Detección de contenido regenerable y copia ligera](#c06) | P2 | Alta en proyectos compatibles | Definir alcance y validar prototipo |
| 9 | A04 | [Copiar lo esencial dentro de un plazo o espacio disponible](#a04) | P2 | Alta cuando existe urgencia o capacidad limitada | Definir alcance y validar prototipo |
| 10 | C16 | [Hora objetivo para completar toda la copia](#c16) | P2 | Alta en trabajos largos programados | Definir alcance y validar prototipo |
| 11 | A02 | [Copiar proyectos con todas sus dependencias](#a02) | P2 | Alta para trabajo creativo y técnico | Definir alcance y validar prototipo |
| 12 | C04 | [Detección de memorias con capacidad falsa](#c04) | P2 | Alta para soportes sospechosos; no de uso universal | Investigar/prototipar con soportes vacíos o respaldados |
| 13 | C09 | [Copias para compartir con metadatos seleccionados retirados](#c09) | P2 | Alta al compartir contenido personal | Definir alcance y validar prototipo |
| 14 | C03 | [Rescate que prioriza archivos importantes y sin otra copia](#c03) | P2 | Muy alta ante fallos, uso infrecuente | Definir alcance y validar prototipo |
| 15 | C02 | [Prioridad contextual para la carpeta que se está consultando](#c02) | P2 | Media, depende del hábito | Definir alcance y validar prototipo |
| 16 | C10 | [Aviso local de posibles datos sensibles](#c10) | P3 | Alta para documentos privados; condicionada a precisión | Validar antes de inversión amplia |
| 17 | C17 | [Sugerencias de automatización a partir de hábitos](#c17) | P3 | Media-alta para tareas repetidas | Validar antes de inversión amplia |
| 18 | A07 | [Conflictos explicados mediante la historia de versiones](#a07) | P3 | Alta para trabajo entre varios equipos | Validar antes de inversión amplia |
| 19 | A06 | [Ida y vuelta conservando nombres y metadatos recuperables](#a06) | P3 | Alta en migraciones concretas | Validar antes de inversión amplia |
| 20 | C14 | [Relevo de una copia en marcha entre equipos](#c14) | P3 | Alta en redes con varios equipos | Validar antes de inversión amplia |
| 21 | A05 | [Continuidad de una entrega entre red y medios físicos](#a05) | P3 | Alta para grandes entregas con mala conexión | Validar antes de inversión amplia |
| 22 | C01 | [Acceso bajo demanda a archivos antes de terminar la transferencia](#c01) | P3 | Potencialmente alta en archivos grandes | Prototipo de viabilidad, sin lanzamiento general |
| 23 | C13 | [Distribución cooperativa de una entrega a muchos equipos](#c13) | P4 | Alta para aulas y despliegues; baja para uso individual | Validar demanda y topología de red |
| 24 | C08 | [Selección asistida de fotos similares o de ráfaga](#c08) | P4 | Alta para importación masiva de fotos | Conservar y validar público específico |
| 25 | C07 | [Conversión multimedia para ajustarse al espacio del destino](#c07) | P4 | Media-alta para copias de consumo | Conservar y validar público específico |
| 26 | C20 | [Archivo familiar y legado digital recuperable](#c20) | P4 | Alta para un propósito específico a largo plazo | Conservar y validar público específico |
| 27 | C19 | [Programación según coste o intensidad de carbono de la energía](#c19) | P4 | Condicionada; beneficio pendiente de medir | Medir beneficio antes de integrar servicios |

## Criterios comunes acordados

1. **Conservar todas las ideas.** Una idea de baja prioridad sigue documentada; una formulación incorrecta se corrige sin borrar la necesidad que intentaba resolver.
2. **Priorizar resultados observables.** Tiempo ahorrado, conjuntos utilizables, fallos detectados y restauraciones reproducibles. No convertir automáticamente un aviso en una supuesta pérdida evitada.
3. **Separar copia exacta y copia derivada.** Quitar información, aceptar revisiones de un documento o transcodificar debe elegirse explícitamente y describirse como transformación. El original se preserva.
4. **Mostrar límites e incertidumbre.** Un muestreo no certifica todo un disco; una copia vista antes no está comprobada hoy; una estimación de llegada no es una garantía.
5. **Control del usuario.** Mostrar el motivo de cambios automáticos y permitir desactivarlos o corregirlos antes de efectos irreversibles. No prometer deshacer universalmente cualquier efecto ya consumado.
6. **Privacidad del catálogo y análisis.** Inventario opcional de ubicaciones elegidas, local, protegido y fácil de limitar o eliminar. Los nombres y rutas también son información sensible. El análisis de contenido de C10 será voluntario y local.
7. **Evaluar con datos locales o pruebas autorizadas.** Las métricas propuestas no requieren activar telemetría ni enviar contenido, nombres o claves.
8. **No inventar exclusividad ni durabilidad.** No afirmar «nadie lo hace», «instantáneo» o «funcionará dentro de veinte años» sin evidencia suficiente. Documentar formatos facilita recuperación futura, pero no garantiza soporte en cualquier herramienta.
9. **Mantener expectativas de integridad.** Archivos parciales, marcadores, archivos rescatados con huecos y archivos completos deben distinguirse claramente.
10. **Partir del producto real.** Reutilizar verificación, recuperación, diario, filtros, sincronización, cifrado y tareas existentes. La existencia de esas piezas no elimina el trabajo de integración y validación.

## Fichas de las 27 propuestas

### P1 — Primera prioridad: confianza y uso cotidiano

Aportan a problemas frecuentes o a la fiabilidad del respaldo. Conviene empezar con versiones acotadas; el catálogo y el mapa son una base estratégica, no una entrega pequeña.

<a id="c12"></a>

#### 1. Prevención de errores peligrosos antes de copiar — C12

**Prioridad:** P1. **Utilidad prevista:** Alta y transversal. **Esfuerzo estimado:** Media. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Comparar el plan con el estado de origen y destino y con tareas anteriores. Advertir de borrados masivos, origen vacío inesperado, inversión probable de unidades o versiones antiguas que sobrescribirían otras nuevas. Ejemplo: «Este espejo quitaría el 80 % del destino».

- **Quién la usaría:** Quienes usan espejo, mueven archivos o repiten copias entre varios discos.
- **Quién probablemente no la necesita:** Quien realiza copias simples sin sustituciones obtiene menos valor; no necesita avisos en cada operación rutinaria.
- **Qué añade a MegaCopier:** Amplía la simulación y las comprobaciones previas existentes; no vuelve a vender como nueva la protección contra copiar una carpeta dentro de sí misma.
- **Primera versión razonable:** Resumen previo de operaciones destructivas con cantidades y motivos; umbrales configurables. Incorporar el historial y el mapa de copias después.
- **Límites y riesgos:** No adivinar intención con certeza ni bloquear operaciones legítimas sin explicación. Evitar fatiga de avisos; un borrado masivo puede ser intencionado.
- **Cómo demostrar utilidad:** Errores detectados en escenarios controlados y proporción de avisos que el usuario considera útiles; no contar todos los avisos como pérdidas evitadas.
- **Base y dependencias:** Plan de copia e historial actuales; mejora con A01/C15.

<a id="c11"></a>

#### 2. Diagnóstico «¿Por qué va lento?» — C11

**Prioridad:** P1. **Utilidad prevista:** Alta y transversal. **Esfuerzo estimado:** Media-alta. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Mostrar dónde se consume el tiempo: leer, escribir, red, preparación, archivos pequeños, verificación o espera. Proponer acciones cuando exista evidencia. Ejemplo: «La escritura en el destino es el límite medido».

- **Quién la usaría:** Usuarios domésticos, técnicos y profesionales que copian volúmenes grandes o muchos archivos pequeños.
- **Quién probablemente no la necesita:** Quien copia unos pocos archivos sin esperas; quien ya dispone de diagnóstico especializado puede obtener menos valor.
- **Qué añade a MegaCopier:** Añade explicación y acciones al autoajuste, gráfica de velocidad y mediciones existentes.
- **Primera versión razonable:** Instrumentar fases y colas sin pruebas invasivas durante la copia; distinguir causa confirmada de hipótesis. Después incorporar enlace USB, temperatura o datos del dispositivo cuando estén disponibles.
- **Límites y riesgos:** No culpar al cable, antivirus o tecnología SMR solo por observar pocos MB/s. No recomendar desactivar protección como solución rutinaria. Las cifras de mejora son ejemplos, no promesas.
- **Cómo demostrar utilidad:** Exactitud de diagnósticos frente a cuellos de botella provocados y mejora observada tras las recomendaciones.
- **Base y dependencias:** Mediciones del motor y capacidades consultables de Windows.

<a id="c18"></a>

#### 3. Estimación de duración basada en historial y tipo de archivos — C18

**Prioridad:** P1. **Utilidad prevista:** Alta en transferencias largas. **Esfuerzo estimado:** Media. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Aprender tiempos por pareja de destinos, tamaños, cantidad de archivos y fases. Mostrar una hora o intervalo de llegada y corregirlo con la velocidad real.

- **Quién la usaría:** Usuarios que planifican copias grandes, archivos pequeños numerosos y trabajos repetidos entre destinos.
- **Quién probablemente no la necesita:** Copias de segundos o destinos sin historial estable aportan poco aprendizaje.
- **Qué añade a MegaCopier:** Mejora el cálculo de duración y la línea de tiempo actuales; la novedad es usar historial contextual y expresar incertidumbre.
- **Primera versión razonable:** Separar coste por archivo y por byte, incluir verificación/preparación, aprendizaje local con caducidad y descarte de sesiones anómalas.
- **Límites y riesgos:** No ocultar cambios reales para producir una cifra estable. El mismo disco cambia con carga, espacio, caché, temperatura o conexión; nunca prometer precisión de minutos desde el primer instante.
- **Cómo demostrar utilidad:** Error absoluto y relativo al inicio, mitad y final, y cobertura real de los intervalos anunciados.
- **Base y dependencias:** Mediciones actuales y, cuando exista, C11; base de C16 y A04.

<a id="c15"></a>

#### 4. Catálogo de discos y búsqueda incluso desconectados — C15

**Prioridad:** P1. **Utilidad prevista:** Alta para colecciones repartidas. **Esfuerzo estimado:** Alta. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Recordar el contenido de ubicaciones autorizadas y buscar «¿Dónde está este archivo?» aunque el disco esté desconectado. Proponer una copia local equivalente como origen más rápido cuando esté disponible y validada.

- **Quién la usaría:** Personas con varios discos externos, fotógrafos, técnicos y quienes archivan por proyectos o años.
- **Quién probablemente no la necesita:** Quien solo usa una carpeta local bien organizada o no desea indexar rutas y nombres privados.
- **Qué añade a MegaCopier:** Añade inventario persistente y búsqueda transversal a la identificación de unidades y la copia desde varios orígenes.
- **Primera versión razonable:** Inventario incremental a partir de copias realizadas y carpetas elegidas, etiquetas de unidades, fecha de observación y actualización al reconectar. La selección de origen alternativo se añade tras verificar identidad y versión.
- **Límites y riesgos:** No escanear todos los discos sin elección ni tratar serie de dispositivo o suma rápida como prueba suficiente. Limitar coste de indexación, proteger el catálogo y permitir excluir o retirar ubicaciones.
- **Cómo demostrar utilidad:** Búsquedas resueltas correctamente, consumo de CPU/disco y detección de entradas desactualizadas.
- **Base y dependencias:** Infraestructura común con A01; las dos entradas se conservan, pero no se presupuestan dos motores independientes.

<a id="a01"></a>

#### 5. Mapa de protección y revisión antes de liberar espacio — A01

**Prioridad:** P1. **Utilidad prevista:** Alta para datos importantes en varios destinos. **Esfuerzo estimado:** Alta. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Relacionar versiones idénticas por contenido y mostrar dónde existen, cuándo se comprobaron y si comparten un mismo disco físico. Antes de liberar espacio, señalar archivos que perderían su última copia conocida.

- **Quién la usaría:** Familias, fotógrafos, autónomos y oficinas con archivos repartidos entre PC, USB, NAS o nube.
- **Quién probablemente no la necesita:** Quien solo usa archivos temporales o no quiere mantener un inventario; tampoco sustituye una política de respaldo profesional.
- **Qué añade a MegaCopier:** El guardián actual comprueba integridad; esta función interpreta la cobertura entre destinos y versiones.
- **Primera versión razonable:** Mapa de carpetas elegidas con estados «visto», «verificado en fecha», «desconectado» y «desconocido». Señalar copias en el mismo dispositivo sin inventar independencia cuando un NAS o la nube ocultan su topología.
- **Límites y riesgos:** Un catálogo antiguo no demuestra que la copia siga existiendo. No autorizar borrados automáticos por coincidencia de nombre/tamaño o un hash parcial. Eliminar la última copia conocida requiere información actual y una decisión explícita.
- **Cómo demostrar utilidad:** Identificación correcta de archivos con una sola copia conocida y de copias en el mismo soporte en un conjunto de referencia.
- **Base y dependencias:** Comparte inventario e identidad con C15; prioridad estratégica alta aunque su construcción sea gradual.

<a id="c05"></a>

#### 6. Verificador y reparador portátil junto a la copia — C05

**Prioridad:** P1. **Utilidad prevista:** Alta para respaldo y archivo. **Esfuerzo estimado:** Media. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Opción para incluir un verificador, manifiesto, instrucciones y paridad, si se solicita. Permitir comprobar y reparar en otro PC compatible sin instalar MegaCopier ni necesitar Internet.

- **Quién la usaría:** Familias que conservan fotos, técnicos y personas que entregan archivos o respaldos a terceros.
- **Quién probablemente no la necesita:** Transferencias temporales, destinatarios que no pueden ejecutar programas o plataformas no compatibles con el ejecutable.
- **Qué añade a MegaCopier:** Empaqueta sumas, manifiestos y reparación existentes en una experiencia autónoma; no inventa otro algoritmo de recuperación.
- **Primera versión razonable:** Herramienta portátil firmada para Windows soportado, informe claro de archivos correctos, faltantes y dañados, reparación limitada por la paridad disponible. Manifiesto en formato documentado para que otra herramienta pueda implementar su lectura.
- **Límites y riesgos:** Un ejecutable no garantiza funcionar dentro de veinte años. La confianza en el verificador y manifiesto debe explicarse; el espacio de paridad es adicional y no repara pérdidas ilimitadas.
- **Cómo demostrar utilidad:** Una persona distinta del creador puede comprobar y recuperar la copia en un PC limpio compatible, sin instrucciones externas.
- **Base y dependencias:** Verificación, manifiestos y recuperación existentes; complementa A03 y sirve de base a C20.

<a id="a03"></a>

#### 7. Ensayos de recuperación de las copias de seguridad — A03

**Prioridad:** P1. **Utilidad prevista:** Alta para respaldo fiable. **Esfuerzo estimado:** Media-alta. **Estado:** propuesta; diseñar una primera versión acotada.

**Qué haría y para qué:** Restaurar archivos elegidos en una ubicación temporal y comprobar catálogo, lectura, reconstrucción, descompresión, descifrado y contenido. Para formatos soportados, validar estructura sin ejecutar macros ni programas.

- **Quién la usaría:** Quienes dependen de respaldos personales o profesionales y necesitan comprobarlos antes de una emergencia.
- **Quién probablemente no la necesita:** Quien solo entrega archivos temporales; pruebas completas frecuentes pueden no compensar en colecciones enormes.
- **Qué añade a MegaCopier:** Prueba el proceso completo de restauración; releer hashes o reparar bloques no demuestra por sí solo que el flujo completo funcione.
- **Primera versión razonable:** Ensayo manual con muestra explícita y opción completa, presupuesto de espacio, informe de cobertura y limpieza de temporales. Programación posterior con límites de recursos.
- **Límites y riesgos:** Una muestra válida no certifica todo el respaldo. Si no se dispone de una clave, informar «no comprobado» sin ocultarlo como éxito. No ejecutar contenido desconocido para probarlo.
- **Cómo demostrar utilidad:** Fallos conocidos detectados en cada etapa y restauraciones reproducibles en un entorno distinto.
- **Base y dependencias:** Restauración existente; complementa C05, no depende de tener todo el catálogo C15.

### P2 — Siguiente prioridad: necesidades concretas de alto valor

Mejoran proyectos, urgencia, privacidad y rescate. Requieren definir público y alcance. C04 tiene prioridad para investigación y prototipo seguro, no autorización para lanzar pruebas de escritura de inmediato.

<a id="c06"></a>

#### 8. Detección de contenido regenerable y copia ligera — C06

**Prioridad:** P2. **Utilidad prevista:** Alta en proyectos compatibles. **Esfuerzo estimado:** Media. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Reconocer proyectos por archivos característicos y proponer omitir cachés o salidas reconstruibles, mostrando ahorro. Mantener perfiles separados para respaldo completo y entrega ligera.

- **Quién la usaría:** Desarrolladores, usuarios de Unity/Unreal y personas que trasladan entornos con cachés voluminosas.
- **Quién probablemente no la necesita:** Quien necesita una copia exacta, trabaja sin Internet, conserva dependencias modificadas o no puede reconstruirlas.
- **Qué añade a MegaCopier:** Sugiere reglas a partir del tipo de proyecto sobre los filtros y .mcignore existentes.
- **Primera versión razonable:** Pocas familias de proyectos, lista revisable de exclusiones y explicación de cómo se regeneran. Conservar manifiestos, archivos de bloqueo y configuraciones necesarias.
- **Límites y riesgos:** .gitignore no equivale a prescindible: puede excluir .env, datos o trabajo local. node_modules, entornos o cachés pueden contener cambios únicos. No omitir nada silenciosamente ni garantizar reconstrucción futura.
- **Cómo demostrar utilidad:** Tiempo y espacio ahorrados en proyectos que siguen pudiendo abrirse o reconstruirse tras el traslado.
- **Base y dependencias:** Filtros existentes; comparte modo proyecto con A02.

<a id="a04"></a>

#### 9. Copiar lo esencial dentro de un plazo o espacio disponible — A04

**Prioridad:** P2. **Utilidad prevista:** Alta cuando existe urgencia o capacidad limitada. **Esfuerzo estimado:** Alta. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** El usuario fija «tengo diez minutos» o «solo caben 20 GB» y marca lo imprescindible. Planificar conjuntos completos y útiles, dejando claro lo que quedará pendiente.

- **Quién la usaría:** Profesionales que salen con un portátil, usuarios con poco espacio y personas que necesitan primero un trabajo concreto.
- **Quién probablemente no la necesita:** Copias pequeñas, respaldos que requieren todos los archivos o usuarios sin prioridades definidas.
- **Qué añade a MegaCopier:** Añade objetivos y dependencias a la reordenación manual de archivos. A diferencia de C16, admite completar solo un subconjunto elegido.
- **Primera versión razonable:** Prioridad explícita por carpeta y presupuesto estimado; integrar dependencias de A02 cuando estén disponibles. Nunca presentar el subconjunto como copia completa.
- **Límites y riesgos:** No inferir valor personal únicamente por extensión ni garantizar plazos físicos. Reordenar no siempre aumenta rendimiento; evitar dejar proyectos sin piezas.
- **Cómo demostrar utilidad:** Conjuntos prioritarios utilizables al vencer el plazo y calidad de la explicación de lo excluido.
- **Base y dependencias:** Cola existente, C18; mejora con A02 y C15.

<a id="c16"></a>

#### 10. Hora objetivo para completar toda la copia — C16

**Prioridad:** P2. **Utilidad prevista:** Alta en trabajos largos programados. **Esfuerzo estimado:** Media-alta. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Definir una hora de finalización; ajustar el ritmo según progreso, uso del equipo y margen restante. Avisar cuando el objetivo sea improbable.

- **Quién la usaría:** Quienes dejan respaldos nocturnos o comparten recursos del equipo mientras una copia debe terminar a cierta hora.
- **Quién probablemente no la necesita:** Quien necesita siempre máxima velocidad, copias breves o conexiones tan variables que no permitan prever una llegada.
- **Qué añade a MegaCopier:** Añade control por resultado al horario y límite de velocidad existentes. A diferencia de A04, conserva el alcance completo.
- **Primera versión razonable:** Control de velocidad con margen y estimación por intervalo, preferencias de molestia y aviso anticipado cuando no alcance.
- **Límites y riesgos:** El programa no puede superar disco o red ni garantizar una hora. No acelerar incumpliendo los límites elegidos por el usuario.
- **Cómo demostrar utilidad:** Cumplimiento de objetivos físicamente viables e impacto en la respuesta de otras aplicaciones.
- **Base y dependencias:** C18, mediciones y limitador existentes; puede compartir planificador con A04.

<a id="a02"></a>

#### 11. Copiar proyectos con todas sus dependencias — A02

**Prioridad:** P2. **Utilidad prevista:** Alta para trabajo creativo y técnico. **Esfuerzo estimado:** Alta por cada familia de formatos. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Detectar recursos vinculados fuera de la carpeta, informar de los ausentes y preparar una entrega utilizable. Ejemplo: incluir vídeos que un proyecto referencia desde otra unidad.

- **Quién la usaría:** Editores de vídeo/audio, diseñadores, profesionales CAD/3D y usuarios de proyectos con recursos externos.
- **Quién probablemente no la necesita:** Quien mueve archivos independientes o proyectos cuyo formato no está soportado.
- **Qué añade a MegaCopier:** Entiende relaciones entre archivos; copiar correctamente una carpeta no encuentra dependencias externas.
- **Primera versión razonable:** Uno o dos formatos documentados, informe de dependencias, paquete con manifiesto y validación de referencias. Cambiar rutas solo mediante adaptación controlada en una copia.
- **Límites y riesgos:** Un analizador no puede entender todos los programas. Fuentes, complementos, licencias o recursos remotos pueden impedir reproducir el entorno. Mostrar lo desconocido y no modificar originales.
- **Cómo demostrar utilidad:** Proyectos que abren con recursos completos en un segundo equipo compatible, frente a una copia de carpeta convencional.
- **Base y dependencias:** Analizadores por formato; se complementa con C06, preservando la elección entre entrega y respaldo.

<a id="c04"></a>

#### 12. Detección de memorias con capacidad falsa — C04

**Prioridad:** P2. **Utilidad prevista:** Alta para soportes sospechosos; no de uso universal. **Esfuerzo estimado:** Alta para una implementación segura. **Estado:** propuesta; investigar/prototipar con soportes vacíos o respaldados.

**Qué haría y para qué:** Ofrecer diagnóstico de capacidad declarado y pruebas adecuadas al soporte. Distinguir muestreo rápido de comprobación extensa y recordar fecha, método y resultado.

- **Quién la usaría:** Compradores de memorias desconocidas, técnicos y personas que reciben USB o tarjetas sin historial fiable.
- **Quién probablemente no la necesita:** Quien tiene datos únicos en un soporte sin copia previa; no conviene iniciar allí una prueba de escritura como trámite rutinario.
- **Qué añade a MegaCopier:** Añade una comprobación específica del soporte antes de confiarle archivos; la verificación normal de una copia tiene otro alcance.
- **Primera versión razonable:** Flujo voluntario para unidades vacías o con datos respaldados, integración o evaluación de herramientas conocidas y reporte de incertidumbre. Resultado: «sin anomalías en la muestra» o «sospechosa»; «no concluyente» si la prueba no permite decidir. Nunca «certificada». No activar pruebas de bajo nivel automáticamente.
- **Límites y riesgos:** No prometer treinta segundos, capacidad exacta ni conservación garantizada de datos bajo firmware fraudulento. Una serie puede repetirse; el resultado caduca. No recomendar «usar solo los primeros GB» de una memoria detectada como falsa.
- **Cómo demostrar utilidad:** Fraudes conocidos detectados, falsos negativos del muestreo y ausencia de pérdida de datos en un laboratorio con fallos y desconexiones.
- **Base y dependencias:** Investigación y pruebas de hardware antes de producto; identificación de dispositivos actual.

<a id="c09"></a>

#### 13. Copias para compartir con metadatos seleccionados retirados — C09

**Prioridad:** P2. **Utilidad prevista:** Alta al compartir contenido personal. **Esfuerzo estimado:** Media en fotos; alta en documentos. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Crear una copia derivada sin ubicación GPS, identificadores de cámara u otras propiedades elegidas. Mantener la propuesta de limpiar Office/PDF como ampliación explícita.

- **Quién la usaría:** Quienes publican fotos o envían documentos a clientes, compradores o personas ajenas.
- **Quién probablemente no la necesita:** Fotógrafos que necesitan EXIF, archivo histórico, trabajo forense o entregas que deben conservar exactamente el original.
- **Qué añade a MegaCopier:** Añade una transformación de privacidad a la copia normal y al cifrado; el original se conserva.
- **Primera versión razonable:** Fotos de formatos concretos, selección de campos y resumen verificable antes/después. Para Office/PDF, vista previa de cambios y pruebas por formato. Considerar retirada de todos los EXIF como opción revisable, preservando la interpretación visual necesaria, como la orientación.
- **Límites y riesgos:** Aceptar revisiones de Word o quitar adjuntos/comentarios cambia contenido; no hacerlo como limpieza invisible. Eliminar EXIF no elimina información visible en la imagen ni garantiza anonimato; puede afectar firmas.
- **Cómo demostrar utilidad:** Campos seleccionados eliminados y contenido visual/documental conservado en formatos soportados.
- **Base y dependencias:** Procesadores por formato; comparte experiencia de entrega con C10.

<a id="c03"></a>

#### 14. Rescate que prioriza archivos importantes y sin otra copia — C03

**Prioridad:** P2. **Utilidad prevista:** Muy alta ante fallos, uso infrecuente. **Esfuerzo estimado:** Alta. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Ante errores relevantes, proponer salvar primero carpetas elegidas y archivos sin otra copia conocida. Posponer zonas difíciles y adaptar reintentos considerando el comportamiento físico.

- **Quién la usaría:** Personas recuperando un disco deteriorado y técnicos que cuentan con prioridades claras del propietario.
- **Quién probablemente no la necesita:** Discos sanos; casos físicos graves que requieran recuperación especializada; quien necesita una imagen forense íntegra.
- **Qué añade a MegaCopier:** Amplía SMART y modo rescate con prioridades y estrategia de lectura; no garantiza salvar todos los archivos importantes.
- **Primera versión razonable:** Selección explícita de carpetas, presupuesto de reintentos, informe de rangos ilegibles y primera pasada sobre datos accesibles. Aprovechar inventario previo sin escanear exhaustivamente el disco enfermo.
- **Límites y riesgos:** Lentitud no demuestra avería. Mover el cabezal entre muchos archivos puede empeorar el rescate; equilibrar importancia y localidad. Registrar archivos incompletos sin presentarlos como íntegros. Una carpeta completa no equivale a sectores físicamente contiguos.
- **Cómo demostrar utilidad:** Archivos prioritarios completos recuperados y lecturas/reintentos necesarios sobre casos controlados.
- **Base y dependencias:** Rescate y salud actuales; mejora con A01/C15 y prioridades A04.

<a id="c02"></a>

#### 15. Prioridad contextual para la carpeta que se está consultando — C02

**Prioridad:** P2. **Utilidad prevista:** Media, depende del hábito. **Esfuerzo estimado:** Media. **Estado:** propuesta; definir alcance y validar prototipo.

**Qué haría y para qué:** Ofrecer priorizar pendientes de una carpeta del destino abierta en el Explorador; el modo automático sería opcional y esperaría una intención estable.

- **Quién la usaría:** Quien navega por el destino de una copia grande buscando archivos que necesita enseguida.
- **Quién probablemente no la necesita:** Copias desatendidas, usuarios que exploran por curiosidad o cargas donde reordenar penaliza discos mecánicos.
- **Qué añade a MegaCopier:** Automatiza parcialmente la reordenación manual con contexto del Explorador; detectar una carpeta abierta no equivale a conocer cada archivo visible.
- **Primera versión razonable:** Acción «Traer esta carpeta primero» y, después, sugerencia al mantenerla abierta. Mantener trabajos actuales y dependencias, sin saltos continuos.
- **Límites y riesgos:** No vigilar actividad general ni alterar prioridades silenciosamente. Sin C01, los archivos aún no copiados no aparecen necesariamente en el Explorador.
- **Cómo demostrar utilidad:** Tiempo hasta disponer de la carpeta solicitada y cantidad de reordenamientos no deseados.
- **Base y dependencias:** Cola y detección del Explorador actuales; opcional en A04.

### P3 — Validar o prototipar antes de comprometer desarrollo amplio

La utilidad potencial es real, pero la precisión, las dependencias o el coste justifican pruebas previas. Se conservan todas las propuestas.

<a id="c10"></a>

#### 16. Aviso local de posibles datos sensibles — C10

**Prioridad:** P3. **Utilidad prevista:** Alta para documentos privados; condicionada a precisión. **Esfuerzo estimado:** Alta. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Buscar localmente patrones de claves privadas, API, tarjetas, DNI/NIE/IBAN y documentos identificativos, con OCR en formatos compatibles. Ofrecer cifrar o revisar antes de compartir.

- **Quién la usaría:** Familias, autónomos y oficinas que copian identificaciones, claves o documentos privados a destinos compartidos o portátiles.
- **Quién probablemente no la necesita:** Colecciones públicas, usuarios que ya aplican controles especializados o quienes no autorizan examinar contenido.
- **Qué añade a MegaCopier:** Usa OCR y cifrado existentes para orientar una decisión de privacidad antes de transferir.
- **Primera versión razonable:** Categorías y formatos limitados, activación voluntaria, destino contextual y explicación del hallazgo sin exponer el secreto completo en registros.
- **Límites y riesgos:** No prometer ausencia de datos sensibles tras un resultado negativo. Hay falsos positivos, falsos negativos, OCR imperfecto y documentos cifrados; no equiparar toda nube o USB con exposición pública.
- **Cómo demostrar utilidad:** Precisión y sensibilidad sobre un corpus de prueba autorizado, coste de análisis y aceptación de avisos.
- **Base y dependencias:** OCR, cifrado y analizadores; integración con C09.

<a id="c17"></a>

#### 17. Sugerencias de automatización a partir de hábitos — C17

**Prioridad:** P3. **Utilidad prevista:** Media-alta para tareas repetidas. **Esfuerzo estimado:** Media. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Detectar repetición y ofrecer una regla concreta, por ejemplo copiar Trabajo cuando se conecte el USB elegido. Preparar parte del plan si compensa.

- **Quién la usaría:** Usuarios que copian las mismas carpetas a un USB recurrentemente y no configuran tareas por iniciativa propia.
- **Quién probablemente no la necesita:** Quien hace copias puntuales o ya dispone de tareas programadas suficientes.
- **Qué añade a MegaCopier:** Facilita crear las tareas existentes; no es un nuevo motor de programación.
- **Primera versión razonable:** Patrones simples en historial local, propuesta revisable y opción de silenciar sugerencias. Revalidar archivos, hashes y destino al ejecutar cualquier plan precalculado.
- **Límites y riesgos:** No automatizar sin aceptación ni confundir coincidencias con hábitos. Una lista preparada ayer puede estar obsoleta y leer anticipadamente puede gastar más de lo que ahorra.
- **Cómo demostrar utilidad:** Sugerencias aceptadas que permanecen útiles, reducción de pasos y tasa de sugerencias descartadas.
- **Base y dependencias:** Historial y tareas existentes; C15 mejora reconocimiento de destinos.

<a id="a07"></a>

#### 18. Conflictos explicados mediante la historia de versiones — A07

**Prioridad:** P3. **Utilidad prevista:** Alta para trabajo entre varios equipos. **Esfuerzo estimado:** Alta. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Mostrar si una versión deriva de otra o si ambas cambiaron desde una base común registrada. Conservar ambas cuando no pueda resolverse con certeza.

- **Quién la usaría:** Quienes editan los mismos documentos entre portátil, escritorio y discos, o sincronizan cambios de varias personas.
- **Quién probablemente no la necesita:** Copias unidireccionales de contenido inmutable o archivos sin historial suficiente.
- **Qué añade a MegaCopier:** Amplía la comparación y el estado de sincronización con procedencia entre versiones; no confunde fecha más reciente con contenido más completo.
- **Primera versión razonable:** Linaje en ubicaciones seguidas, explicación de divergencias y conservación de ambas. Cualquier combinación de contenido requiere soporte específico y decisión aparte.
- **Límites y riesgos:** Sin una base registrada no se puede reconstruir genealogía por intuición. Documentos binarios no se fusionan de forma general; mover archivos fuera de MegaCopier puede dejar huecos.
- **Cómo demostrar utilidad:** Conflictos y divergencias correctamente explicados en un historial conocido, sin pérdida de ramas.
- **Base y dependencias:** Estado de sincronización/versiones, identidad de contenido; inventario de C15 puede compartirse pero no basta por sí solo.

<a id="a06"></a>

#### 19. Ida y vuelta conservando nombres y metadatos recuperables — A06

**Prioridad:** P3. **Utilidad prevista:** Alta en migraciones concretas. **Esfuerzo estimado:** Media-alta. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Guardar un manifiesto auxiliar con nombres originales y metadatos que el destino no admite, para restaurarlos al regresar a un entorno compatible.

- **Quién la usaría:** Archiveros, técnicos y usuarios que trasladan datos entre sistemas de archivos o servicios con limitaciones diferentes.
- **Quién probablemente no la necesita:** Transferencias entre destinos plenamente compatibles o destinatarios que solo necesitan el contenido.
- **Qué añade a MegaCopier:** Hace reversibles determinadas adaptaciones de nombres y pérdidas de metadatos actualmente advertidas.
- **Primera versión razonable:** Nombres y fechas de formatos definidos, mapeo sin colisiones e informe de qué se guardó y qué no. ACL, flujos y enlaces se incorporan con reglas específicas.
- **Límites y riesgos:** El paquete auxiliar puede perderse y contener información privada. No aplicar permisos de otro equipo de forma ciega ni prometer equivalencia universal de sistemas de archivos.
- **Cómo demostrar utilidad:** Pruebas de ida y vuelta con igualdad de las propiedades declaradas como recuperables.
- **Base y dependencias:** Normalización de nombres y preservación de metadatos existentes; debe distinguirse de C09, que elimina propiedades deliberadamente.

<a id="c14"></a>

#### 20. Relevo de una copia en marcha entre equipos — C14

**Prioridad:** P3. **Utilidad prevista:** Alta en redes con varios equipos. **Esfuerzo estimado:** Alta. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Transferir el estado confirmado a otro PC autorizado que pueda acceder al mismo origen y destino, continuar y avisar al terminar.

- **Quién la usaría:** Usuarios que migran datos entre dos NAS o ubicaciones compartidas y quieren apagar el portátil iniciador.
- **Quién probablemente no la necesita:** Quien copia entre discos conectados solo a su portátil o no dispone de otro equipo ejecutor compatible.
- **Qué añade a MegaCopier:** Extiende la reanudación tras interrupción a un cambio coordinado de ejecutor.
- **Primera versión razonable:** Dos equipos Windows compatibles, pausa confirmada, verificación de identidades, bloqueo de un único escritor y continuidad desde estado validado. Credenciales autorizadas en cada equipo.
- **Límites y riesgos:** Un NAS no ejecuta automáticamente una aplicación Windows. Evitar doble escritura, cambios del origen y problemas de permisos; el estado del diario no se puede confiar sin comprobar.
- **Cómo demostrar utilidad:** Relevo y recuperación ante fallos sin duplicación destructiva ni corrupción, con tiempo de interrupción medido.
- **Base y dependencias:** Diario, identificación, protocolo entre pares y nueva coordinación; comparte identidad de entrega con A05.

<a id="a05"></a>

#### 21. Continuidad de una entrega entre red y medios físicos — A05

**Prioridad:** P3. **Utilidad prevista:** Alta para grandes entregas con mala conexión. **Esfuerzo estimado:** Muy alta. **Estado:** propuesta; validar antes de inversión amplia.

**Qué haría y para qué:** Comenzar por red, incorporar contenido transportado en un SSD y completar después únicamente lo restante. La entrega mantiene identidad y manifiesto entre sesiones y medios.

- **Quién la usaría:** Productoras, equipos remotos y técnicos que mueven cientos de GB/TB entre ubicaciones.
- **Quién probablemente no la necesita:** Transferencias pequeñas, redes rápidas estables o usuarios sin necesidad de transportar soportes.
- **Qué añade a MegaCopier:** No es solo leer varios orígenes simultáneamente: coordina una misma versión entregada por distintos caminos y momentos.
- **Primera versión razonable:** Manifiesto inmutable, importación de bloques desde un disco y reanudación por red con verificación. Definir qué ocurre cuando cambia el origen.
- **Límites y riesgos:** Mayor complejidad de estados, espacio temporal y confianza en bloques. Aclarar cifrado y gestión de claves de cada medio. No mezclar versiones con el mismo nombre.
- **Cómo demostrar utilidad:** Bytes de red evitados, tiempo hasta la entrega íntegra y éxito al interrumpir o cambiar medios.
- **Base y dependencias:** Manifiestos, hashes, reanudación y coordinación; puede compartir componentes con C14/C13.

<a id="c01"></a>

#### 22. Acceso bajo demanda a archivos antes de terminar la transferencia — C01

**Prioridad:** P3. **Utilidad prevista:** Potencialmente alta en archivos grandes. **Esfuerzo estimado:** Muy alta. **Estado:** propuesta; prototipo de viabilidad, sin lanzamiento general.

**Qué haría y para qué:** Crear marcadores y traer primero los rangos solicitados al abrir un archivo, manteniendo la copia de fondo hasta convertirlo en contenido completo disponible sin el origen.

- **Quién la usaría:** Usuarios de vídeo y proyectos grandes que necesitan empezar a consultar contenido mientras se transfiere.
- **Quién probablemente no la necesita:** Quien necesita retirar enseguida el origen, usar destinos no soportados o garantizar que todo lo visible está físicamente completo.
- **Qué añade a MegaCopier:** Introduce un modo de disponibilidad progresiva; reanudar un archivo parcial no lo hace utilizable por otras aplicaciones.
- **Primera versión razonable:** Prototipo restringido de Cloud Filter sobre NTFS compatible, primer escenario de lectura controlada, indicadores inequívocos de contenido pendiente y pruebas de cortes. En el prototipo inicial, excluir todo-o-nada, archivos cifrados de MegaCopier y salida ZIP/7z; no asumir compatibilidad de destinos NTFS sin comprobarla.
- **Límites y riesgos:** No llamarlo copia instantánea ni garantizar apertura en segundos. Resolver escrituras, cancelación, antivirus, proveedor ausente y relación con el modo todo-o-nada. El usuario debe saber qué depende todavía del origen.
- **Cómo demostrar utilidad:** Tiempo hasta primera utilización, compatibilidad de aplicaciones y ausencia de corrupción tras fallos; comparar con priorizar un archivo completo.
- **Base y dependencias:** Validar Cloud Filter, recuperación y experiencia de usuario antes de integración general.

### P4 — Conservar para públicos especializados o valor aún por demostrar

Podrían ser prioritarias si esos públicos se convierten en el objetivo del producto. Su posición actual no equivale a descartarlas.

<a id="c13"></a>

#### 23. Distribución cooperativa de una entrega a muchos equipos — C13

**Prioridad:** P4. **Utilidad prevista:** Alta para aulas y despliegues; baja para uso individual. **Esfuerzo estimado:** Muy alta. **Estado:** propuesta; validar demanda y topología de red.

**Qué haría y para qué:** Repartir bloques entre receptores autorizados para que se ayuden, verificando la entrega final. Evaluar códigos de recuperación como RaptorQ solo si aportan valor medido.

- **Quién la usaría:** Aulas, laboratorios, oficinas, eventos y equipos que distribuyen el mismo contenido a numerosos PC.
- **Quién probablemente no la necesita:** Usuarios con uno o dos equipos o redes que prohíben tráfico entre clientes.
- **Qué añade a MegaCopier:** Amplía el envío entre pares y varios orígenes hacia distribución coordinada uno-a-muchos.
- **Primera versión razonable:** Grupo pequeño en LAN cableada, autenticación, control de ancho de banda, verificación de bloques y recuperación de salidas/entradas de equipos.
- **Límites y riesgos:** Wi-Fi compartido puede empeorar; la red sigue imponiendo límites. No prometer treinta receptores en el tiempo de uno. Evaluar licencias y complejidad de tecnologías auxiliares.
- **Cómo demostrar utilidad:** Tiempo y tráfico total frente a envíos individuales en topologías reales, con equipos lentos o desconectados.
- **Base y dependencias:** Protocolo entre pares y planificador de bloques; público objetivo confirmado antes de inversión.

<a id="c08"></a>

#### 24. Selección asistida de fotos similares o de ráfaga — C08

**Prioridad:** P4. **Utilidad prevista:** Alta para importación masiva de fotos. **Esfuerzo estimado:** Alta. **Estado:** propuesta; conservar y validar público específico.

**Qué haría y para qué:** Agrupar imágenes cercanas por tiempo y parecido; sugerir nitidez y, opcionalmente, ojos abiertos. Conservar opciones de copiar todas, seleccionar o separar mejores y resto.

- **Quién la usaría:** Fotógrafos de eventos/deporte y familias con grandes ráfagas que quieran revisar selecciones.
- **Quién probablemente no la necesita:** Archivo integral, evidencia, fotografía artística donde la preferencia es subjetiva o usuarios que ya hacen selección en su editor.
- **Qué añade a MegaCopier:** Amplía la omisión de duplicados exactos hacia similitud visual y selección humana.
- **Primera versión razonable:** Agrupación perceptiva y nitidez, vista comparativa, selección revisable y originales intactos. Validar disponibilidad y requisitos de modelos para rostros/ojos.
- **Límites y riesgos:** Parecido no significa duplicado y nitidez no equivale a valor emocional. No descartar recuerdos automáticamente; evitar falsas promesas de API disponible en todo PC.
- **Cómo demostrar utilidad:** Reducción de tiempo de selección y acuerdo con el usuario, registrando las fotos valiosas que habría omitido.
- **Base y dependencias:** Procesamiento de imágenes local y pruebas con fotógrafos; función opcional especializada.

<a id="c07"></a>

#### 25. Conversión multimedia para ajustarse al espacio del destino — C07

**Prioridad:** P4. **Utilidad prevista:** Media-alta para copias de consumo. **Esfuerzo estimado:** Muy alta. **Estado:** propuesta; conservar y validar público específico.

**Qué haría y para qué:** Estimar si versiones alternativas de vídeo/fotos podrían caber y ofrecer conversión a formatos compatibles, usando hardware cuando esté disponible y preservando originales.

- **Quién la usaría:** Quienes preparan vídeos/fotos para móviles o soportes limitados y aceptan generar versiones derivadas.
- **Quién probablemente no la necesita:** Respaldo exacto, archivo maestro, trabajo profesional que exige originales o dispositivos sin compatibilidad con los formatos elegidos.
- **Qué añade a MegaCopier:** Añade transformación multimedia; la compresión ZIP/7z actual conserva bytes y tiene otro propósito.
- **Primera versión razonable:** Un flujo de vídeo y dispositivos concretos, estimación por muestras, presupuesto de tamaño, control de calidad y verificación de reproducción. Fotos y más códecs después.
- **Límites y riesgos:** No prometer «sin pérdida visible» para todo contenido. El tamaño final, HDR, color, audio, metadatos y compatibilidad requieren control. Considerar licencias de códecs, coste energético y alternativa sin GPU.
- **Cómo demostrar utilidad:** Archivos que caben, reproducen correctamente y alcanzan la calidad elegida, con tiempos medidos.
- **Base y dependencias:** Codificadores/decodificadores y pruebas por dispositivo; no necesario para el motor de copia exacta.

<a id="c20"></a>

#### 26. Archivo familiar y legado digital recuperable — C20

**Prioridad:** P4. **Utilidad prevista:** Alta para un propósito específico a largo plazo. **Esfuerzo estimado:** Alta. **Estado:** propuesta; conservar y validar público específico.

**Qué haría y para qué:** Preparar un archivo cifrado, carta explicativa y verificador; permitir acceso mediante claves de destinatarios o reparto de secreto, por ejemplo dos de tres personas. Revisiones periódicas cuando el soporte esté disponible.

- **Quién la usaría:** Familias y personas que quieren dejar fotos/documentos importantes con instrucciones de acceso para personas de confianza.
- **Quién probablemente no la necesita:** Transferencias diarias, quien no mantendrá claves/soportes o situaciones que requieran una gestión jurídica especializada.
- **Qué añade a MegaCopier:** Combina cifrado, conservación y recuperación en una experiencia de transmisión familiar; no es un nuevo tipo de copia básica.
- **Primera versión razonable:** Formato documentado, instrucciones comprensibles y ensayo real con destinatarios; reparto de claves con bibliotecas revisadas y recuperación comprobada antes de ofrecerlo como opción.
- **Límites y riesgos:** Shamir distribuye un secreto, no autentica herederos ni garantiza ejecución futura. Pérdida de participaciones, cifrado, envejecimiento del soporte y compatibilidad deben gestionarse. No prometer apertura dentro de veinte años sin mantenimiento.
- **Cómo demostrar utilidad:** Destinatarios pueden recuperar el contenido sin ayuda del creador, hoy y en revisiones periódicas documentadas.
- **Base y dependencias:** C05/A03, formatos duraderos, cifrado y plan de mantenimiento.

<a id="c19"></a>

#### 27. Programación según coste o intensidad de carbono de la energía — C19

**Prioridad:** P4. **Utilidad prevista:** Condicionada; beneficio pendiente de medir. **Esfuerzo estimado:** Media-alta. **Estado:** propuesta; medir beneficio antes de integrar servicios.

**Qué haría y para qué:** Programar copias/revisiones en ventanas de menor precio o intensidad de carbono, con hora límite, preferencias de batería y temperatura. Precio y carbono son objetivos distintos.

- **Quién la usaría:** Usuarios con tarifas variables, grandes trabajos flexibles y equipos que ya permanecerán encendidos.
- **Quién probablemente no la necesita:** Tarifas planas, copias urgentes, trabajos pequeños o usuarios que tendrían que mantener un PC encendido solo para esperar.
- **Qué añade a MegaCopier:** Añade criterios energéticos al horario, inactividad y pausa con batería existentes.
- **Primera versión razonable:** Ventanas configuradas manualmente y estimación de energía incremental; después fuentes regionales opcionales y comportamiento sin conexión.
- **Límites y riesgos:** Ahorrar tarifa puede aumentar energía total si se alarga el encendido. Datos externos pueden requerir cuenta, coste o ubicación; explicar consultas y no prometer ahorro universal.
- **Cómo demostrar utilidad:** Coste/energía incremental real frente a ejecutar al momento y cumplimiento del plazo, incluyendo el tiempo de espera encendido.
- **Base y dependencias:** Programador y C16; estudiar demanda y datos regionales antes de conectar servicios.

## Familias compartidas, sin perder ninguna propuesta

| Familia | Entradas conservadas | Qué se comparte y qué sigue siendo distinto |
|---|---|---|
| Inventario y protección | C15, A01; después A07 | C15 localiza archivos; A01 evalúa cobertura de versiones; A07 explica divergencias. Un inventario no prueba por sí solo parentesco entre versiones. |
| Recuperación comprobable | C05, A03; después C20 | C05 lleva herramientas al destinatario; A03 ejecuta un ensayo real; C20 prepara acceso y mantenimiento familiar. |
| Rendimiento y planificación | C11, C18, A04, C16, C02, C03, C17 | C11 explica, C18 estima, A04 prioriza un subconjunto, C16 conserva el objetivo completo, C02 usa contexto, C03 atiende fallos y C17 propone tareas. El rescate necesita su propia estrategia. |
| Proyectos portátiles | A02, C06 | A02 añade dependencias necesarias; C06 propone omitir contenido reconstruible según el propósito elegido. |
| Entregas privadas o fieles | C09, C10, A06 | C09 retira propiedades elegidas; C10 avisa de contenido potencialmente sensible; A06 preserva información para recuperarla al volver. No deben confundirse sus propósitos. |
| Entrega distribuida | C14, A05, C13 | Cambiar ejecutor, cambiar medio y repartir a muchos receptores son alcances distintos con posibles manifiestos y bloques comunes. |
| Experiencias especializadas | C01, C04, C07, C08, C19 | Disponibilidad progresiva, diagnóstico de soporte, conversión, selección fotográfica y energía tienen riesgos y validaciones propios. |

## Secuencia orientativa de construcción

1. **Primer incremento:** prevención de errores (C12), diagnóstico acotado (C11) y mediciones para estimación histórica (C18). No esperar al catálogo completo para mostrar consecuencias de una operación.
2. **Base estratégica:** inventario incremental y privacidad (C15), seguido del mapa de cobertura (A01). Diseñar la identidad de archivos y estados de verificación juntos.
3. **Recuperación:** verificador portátil (C05) y ensayos (A03). Pueden compartir manifiestos; A03 también puede empezar sobre la restauración actual sin esperar toda la herramienta portátil.
4. **Flujos concretos:** exclusiones propuestas (C06), presupuestos/plazos (A04/C16) y primeros formatos con dependencias (A02). Evitar un «detector universal» antes de validar unos pocos casos.
5. **P2 condicionado:** C04 se investiga con soportes vacíos o respaldados, no como prueba automática sobre datos únicos. C09 empieza con metadatos fotográficos. C03 se valida con fallos controlados. C02 empieza como acción explícita.
6. **Resto del catálogo:** avanzar según demanda y resultados de prototipos. Mantener las propuestas P3/P4 en este documento y actualizar su prioridad con evidencia.

No se fija una fecha ni una versión de lanzamiento. Antes de comprometer una función, deben revisarse las partes del motor implicadas y las pruebas reales pendientes de la base actual.

## Formulaciones originales que se conservan como objetivo, pero no como promesa

| Propuesta | Formulación que debe corregirse | Compromiso razonable |
|---|---|---|
| C01 | «Todos aparecen enseguida y se abren en segundos» | Investigar disponibilidad bajo demanda; indicar dependencia del origen y tiempos observados. |
| C03 | «Lo personal llega entero antes de que muera el disco» | Priorizar datos elegidos sin garantizar supervivencia ni cantidad recuperada. |
| C04 | «Treinta segundos, deja todo intacto y descubre exactamente la capacidad real» | Muestreo limitado o prueba extensa; explicar alcance, duración variable y riesgo. No usar una memoria fraudulenta para datos importantes. |
| C06 | «Lo ignorado se regenera» | Revisar cada regla y conservar datos esenciales, configuraciones y cambios locales. |
| C07 | «Cabe con calidad prácticamente igual» | Ofrecer una conversión con posible pérdida, estimación y validación del resultado. |
| C08 | «Solo quedan las mejores» | Selección asistida revisable; la preferencia final es humana. |
| C09 | «Aceptar cambios de Office es limpiar metadatos» | Separar propiedades de cambios reales de contenido y mostrarlos antes de aplicarlos. |
| C10 | «No hay datos sensibles» | «No se detectaron coincidencias en los formatos y categorías analizados». |
| C11 | «Sé que es el cable o antivirus» | Diferenciar evidencia medida de hipótesis; no atribuir una causa sin datos. |
| C13 | «Treinta equipos tardan lo mismo que uno» | Medir beneficio por topología; RaptorQ es una opción de investigación posterior. |
| C14 | «Cualquier NAS puede terminarla» | Otro ejecutor compatible con acceso y permisos; un NAS puede ser solo origen/destino. |
| C15/A01 | «Ya está en otro disco, puedes borrar» | Mostrar identidad, versión, fecha de verificación y disponibilidad; la eliminación sigue siendo una decisión separada. |
| C16/C18 | «Llegada exacta garantizada» | Intervalo estimado, margen, actualización y aviso cuando el objetivo deje de ser viable. |
| C05/C20 | «El EXE seguirá abriendo dentro de veinte años» | Formatos documentados, claves accesibles, verificaciones periódicas y herramientas mantenibles. |
| C19 | «Esperar siempre ahorra» | Comparar coste y energía incremental, incluido mantener el equipo encendido. |

## Trazabilidad de las listas originales

Los títulos se han normalizado para evitar promesas no demostradas; se conserva el alcance propuesto y se señalan sus límites. Las agrupaciones del debate no sustituyen estas entradas.

| Origen | ID | Idea original | Posición final |
|---|---|---|---|
| Codex / Astra | A01 | Mapa de protección y revisión antes de liberar espacio | 5 (P1) |
| Codex / Astra | A02 | Copiar proyectos con todas sus dependencias | 11 (P2) |
| Codex / Astra | A03 | Ensayos de recuperación de las copias de seguridad | 7 (P1) |
| Codex / Astra | A04 | Copiar lo esencial dentro de un plazo o espacio disponible | 9 (P2) |
| Codex / Astra | A05 | Continuidad de una entrega entre red y medios físicos | 21 (P3) |
| Codex / Astra | A06 | Ida y vuelta conservando nombres y metadatos recuperables | 19 (P3) |
| Codex / Astra | A07 | Conflictos explicados mediante la historia de versiones | 18 (P3) |
| Claude | C01 | Acceso bajo demanda a archivos antes de terminar la transferencia | 22 (P3) |
| Claude | C02 | Prioridad contextual para la carpeta que se está consultando | 15 (P2) |
| Claude | C03 | Rescate que prioriza archivos importantes y sin otra copia | 14 (P2) |
| Claude | C04 | Detección de memorias con capacidad falsa | 12 (P2) |
| Claude | C05 | Verificador y reparador portátil junto a la copia | 6 (P1) |
| Claude | C06 | Detección de contenido regenerable y copia ligera | 8 (P2) |
| Claude | C07 | Conversión multimedia para ajustarse al espacio del destino | 25 (P4) |
| Claude | C08 | Selección asistida de fotos similares o de ráfaga | 24 (P4) |
| Claude | C09 | Copias para compartir con metadatos seleccionados retirados | 13 (P2) |
| Claude | C10 | Aviso local de posibles datos sensibles | 16 (P3) |
| Claude | C11 | Diagnóstico «¿Por qué va lento?» | 2 (P1) |
| Claude | C12 | Prevención de errores peligrosos antes de copiar | 1 (P1) |
| Claude | C13 | Distribución cooperativa de una entrega a muchos equipos | 23 (P4) |
| Claude | C14 | Relevo de una copia en marcha entre equipos | 20 (P3) |
| Claude | C15 | Catálogo de discos y búsqueda incluso desconectados | 4 (P1) |
| Claude | C16 | Hora objetivo para completar toda la copia | 10 (P2) |
| Claude | C17 | Sugerencias de automatización a partir de hábitos | 17 (P3) |
| Claude | C18 | Estimación de duración basada en historial y tipo de archivos | 3 (P1) |
| Claude | C19 | Programación según coste o intensidad de carbono de la energía | 27 (P4) |
| Claude | C20 | Archivo familiar y legado digital recuperable | 26 (P4) |

## Revisión conjunta y puntos del acuerdo

- El usuario pidió conservar todas las propuestas, ordenarlas y explicar utilidad y destinatarios.
- Codex contrastó las 20 propuestas de Claude con sus 7 propuestas y la documentación/código local. La revisión fue conceptual; no se implementaron ni probaron estas funciones futuras.
- Se conversó en la sesión abierta de Claude. Claude aceptó conservar los 27 identificadores y los límites técnicos; propuso cuatro cambios de nivel: C04 a P2, C10 y C17 a P3, C13 a P4.
- Codex aceptó esos cambios condicionando C04 a investigación y validación segura, y precisando que un muestreo, una decisión automática o un formato documentado no permiten promesas universales.
- Se incorporaron privacidad del catálogo, decisiones visibles/controlables y separación entre prioridad y orden técnico de construcción.
- **Confirmación final de los matices:** Claude respondió «Cerramos el acuerdo. Acepto los seis matices tal como los planteas», confirmó el alcance de C04, las decisiones controlables, las métricas, los límites de C03/C13/C05 y que A03 puede empezar con la restauración existente. También aceptó la estructura de las 27 fichas.
- **Revisión del archivo:** Claude declaró haber leído el documento completo y confirmó las 27 entradas, su orden, los campos y los límites acordados. Pidió explicitar los resultados de C04 y precisar la lectura del manifiesto de C05; ambas correcciones se incorporaron. También se simplificó la referencia a su sesión. Dio conformidad condicionada a esos ajustes, ya aplicados.
- Este acuerdo es una recomendación de producto de los dos asistentes, no validación con clientes ni autorización para implementar todas las funciones.

## Referencias y base consultada

- [Características actuales de MegaCopier](Caracteristicas.md) y [comparativa/pendientes](Comparativa-copiadores.md): base local; no se reutilizan afirmaciones de exclusividad sin comprobación independiente.
- [Comprobaciones previas del motor](../src/MegaCopier.Engine/CopyEngine.Preflight.cs), [restauración por bloques](../src/MegaCopier.Engine/Backup/BackupRepository.cs), [guardián de integridad](../src/MegaCopier.App/Services/IntegrityGuard.cs), [línea de tiempo](../src/MegaCopier.Engine/Jobs/QueueTimeline.cs) y [carpetas todo-o-nada](../src/MegaCopier.Engine/CopyEngine.Atomic.cs): ejemplos de piezas revisadas; no auditoría exhaustiva.
- [ValiDrive, descripción del muestreo](https://www.grc.com/validrive.htm): sustenta distinguir comprobación rápida de cobertura total (C04).
- [Git: significado de gitignore](https://git-scm.com/docs/gitignore.html): ignorado por Git no implica regenerable (C06).
- [GNU ddrescue: estrategia y movimientos del cabezal](https://www.gnu.org/software/ddrescue/manual/ddrescue_manual.html): fundamento de las cautelas del rescate (C03).
- [Microsoft: archivos marcadores y Cloud Files](https://learn.microsoft.com/en-us/windows/win32/cfapi/build-a-cloud-file-sync-engine): infraestructura y estados de disponibilidad de C01.
- [Autodesk: eTransmit y dependencias](https://help.autodesk.com/cloudhelp/2022/ENU/Revit-DocumentPresent/files/GUID-2317F99F-070C-4D05-A37B-5D9488B35376.htm): antecedente del traslado de proyectos (A02).
- [Veeam: modos de verificación de recuperación](https://helpcenter.veeam.com/docs/vbr/userguide/surebackup_hiw.html?ver=13): antecedente de ensayos de recuperación (A03).

Las fuentes respaldan conceptos y límites concretos, no demuestran que MegaCopier pueda integrarlos sin trabajo adicional. Los ahorros, demanda, costes y compatibilidades deberán medirse.

