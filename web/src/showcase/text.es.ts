import type { ShowcaseText } from './text';

/**
 * Approved by the author word for word (SPEC 0093 R6), except the note about the data: some customers are real institutions.
 * The story of the About page is the author's, with the changes the author asked for (SPEC 0096 R4).
 */
export const es: ShowcaseText = {
  tagline: 'Asignación legal de conductores y autobuses',
  cover: {
    title: 'Nunca guarda una asignación ilegal. Intenta que lo haga.',
    problem:
      'Lo difícil no es encontrar a alguien libre, sino a alguien que pueda conducir legalmente: pausas, descansos y límites de horas (CE 561/2006).\nFleet DispatchGuard lo comprueba antes de guardar y, si no se cumple, dice qué regla y por qué.',
    breakTheLaw: 'Intenta saltarte la ley',
    tryIt: 'Probar',
    howItIsBuilt: 'Cómo está hecho',
    about: 'Sobre mí',
    technologies: 'Tecnologías',
    notes: (wait, measuredOn) => [
      'Sin registro.',
      'Datos de ejemplo, que se regeneran cada noche.',
      `La primera entrada despierta la base de datos: ${wait} en la última medición (${measuredOn}).`,
    ],
  },
  evidence: {
    title: 'Pruebas, no adjetivos',
    intro: 'Cada cifra se ha medido de nuevo para esta página y dice qué test o herramienta la reproduce.',
    items: {
      bursts: {
        figure: '60 de 480',
        claim: 'Asignaciones en choque aceptadas: exactamente una en cada ráfaga.',
        method:
          '60 ráfagas de 8 peticiones simultáneas que dan a un conductor ocho servicios a la misma hora, repartidas entre dos instancias de la API sobre un PostgreSQL 17 real. Las otras 420 se rechazan por solape.',
      },
      oracle: {
        figure: '0 de 10.102',
        claim: 'Discrepancias con una lectura independiente del artículo 6 (tiempos de conducción).',
        method:
          'Calendarios aleatorios de cuatro semanas alrededor de los dos cambios de hora de 2026 y de una semana normal. En cada servicio ofrecido, el motor debe citar los mismos artículos que un oráculo escrito a partir del texto del art. 6, que no comparte código con él.',
      },
      speed: {
        figure: '4,4 µs',
        claim: 'Para comprobar a un conductor con dos semanas de historial frente al CE 561/2006.',
        method:
          'BenchmarkDotNet, compilación Release, .NET 10, en un AMD Ryzen 7 7800X3D. Un conductor nuevo, 1,3 µs. Es la comprobación que se hace con cada candidato antes de proponer o asignar.',
      },
    },
    measuredOn: date => `Medido en ${date}`,
    reproduce: 'Cómo reproducirlo',
    readTheCode: 'Ver el código',
  },
  flow: {
    title: 'Cómo se decide una asignación',
    description:
      'Una petición de asignación bloquea el servicio, el conductor y el autobús. Después, en este orden: 1, el conductor está activo y sin baja durante el servicio; 2, CE 561/2006, incluidos los servicios posteriores que ya tiene asignados; 3, el autobús está activo, tiene plazas suficientes y no está en el taller; 4, ni el conductor ni el autobús tienen otro servicio que se solape. Al primer no, no se guarda nada. Solo el paso 2 se puede forzar, y lo hace una persona que da su nombre y un motivo. Si todo se cumple, se guarda la asignación. El intento se registra siempre.',
    request: { name: 'Petición de asignación', detail: 'Bloquea servicio, conductor y bus' },
    steps: {
      driver: { name: '1. Conductor disponible', detail: 'Activo y sin baja en su horario' },
      compliance: { name: '2. CE 561/2006', detail: 'Pausas, descansos y posteriores' },
      vehicle: { name: '3. Autobús apto', detail: 'Activo, plazas, fuera de taller' },
      overlap: { name: '4. Sin solape', detail: 'Conductor y luego autobús' },
    },
    forcible: 'Forzable con nombre y motivo',
    rejected: ['No:', 'rechazo'],
    saved: { name: 'Se guarda', detail: 'El intento se registra siempre' },
    caption:
      'Al primer «no» no se guarda nada y el intento queda registrado con su motivo. Solo el paso 2 se puede saltar, y lo hace una persona que da su nombre y un motivo: el motor valida igual y el viaje queda marcado como «Forzado». Un conductor no disponible, un autobús no apto o un solape no se fuerzan nunca, y una recomendación tampoco.',
  },
  about: {
    back: 'Portada',
    title: 'Sobre mí',
    contact: 'Contacto',
    story: [
      'Construí Fleet DispatchGuard con IA generativa (Claude Code) para practicar con las herramientas que está adoptando el sector. La IA escribe código; la ingeniería es mía. Escribo la especificación y apruebo el plan antes de que exista código, las reglas legales se prueban primero con sus casos límite, y nada se cierra sin compilar limpio y con los tests en verde contra un PostgreSQL real. El diseño sigue Clean Architecture, diseño guiado por el dominio (DDD) y SOLID. El problema que resuelve: asignar conductor y autobús sin incumplir el Reglamento (CE) n.º 561/2006.',
    ],
    architecture: {
      title: 'Arquitectura, patrones y tecnología',
      intro: 'Las decisiones que dan forma al código, cada una con su porqué. Hay más, también con su porqué, en la página de cómo está hecho.',
      points: [
        {
          title: 'Clean Architecture, cuatro capas',
          why: 'Dominio, aplicación, infraestructura y API, con las dependencias hacia dentro: las reglas del Reglamento no conocen ni la base de datos ni HTTP, así que se prueban sin nada alrededor.',
        },
        {
          title: 'Diseño guiado por el dominio',
          why: 'El servicio es el agregado que acepta o rechaza a su conductor y su autobús; las franjas horarias y el resultado de cada comprobación son objetos de valor, y las cifras legales viven en un único fichero.',
        },
        {
          title: 'Resultados como valores, excepciones en un solo punto',
          why: 'Cada comprobación devuelve un valor que dice qué se incumple y por qué; solo el servicio que asigna convierte un «no» en una excepción de dominio, así que el orden de las comprobaciones está en un solo sitio.',
        },
        {
          title: 'Repositorio y unidad de trabajo',
          why: 'Los repositorios nunca guardan: una unidad de trabajo confirma todo o nada, dentro de los bloqueos del servicio, el conductor y el autobús.',
        },
        {
          title: 'Bloqueos de PostgreSQL, siempre en el mismo orden',
          why: 'Bloqueos consultivos de transacción por servicio, conductor y autobús, tomados por id: dos asignaciones no pueden cruzarse, tampoco con varias instancias.',
        },
        {
          title: 'Un catálogo bilingüe',
          why: 'Todo texto que lee una persona, en la API y en la web, sale de un catálogo en inglés y español; el código decide con códigos, nunca con textos.',
        },
      ],
      technologies: [
        { group: 'Backend', items: ['.NET 10 · Minimal APIs', 'Entity Framework Core', 'PostgreSQL 17'] },
        { group: 'Tests', items: ['xUnit', 'Testcontainers', 'BenchmarkDotNet', 'Vitest', 'Playwright'] },
        { group: 'Web', items: ['React 18 · TypeScript', 'Vite', 'Tailwind CSS', 'ESLint'] },
        { group: 'Despliegue', items: ['Una sola imagen Docker para API y web', 'Vercel', 'Neon'] },
      ],
    },
    role: {
      title: 'Mi papel y el de la IA',
      intro: 'Estas decisiones son mías: las elegí, las puedo defender y cada una la comprueba algo distinto de mi palabra.',
      decisions: [
        {
          title: 'Rechazar antes de guardar',
          why: 'Una asignación ilegal nunca se guarda para corregirla después. Solo se puede forzar un incumplimiento del CE 561/2006, por una persona que da su nombre y un motivo; el intento queda registrado. Un bus no apto, un solape o un conductor no disponible no se fuerzan nunca.',
        },
        {
          title: 'Un oráculo independiente del artículo 6',
          why: 'El motor se contrasta con una segunda lectura del artículo 6 escrita desde el texto, sin código en común: 0 discrepancias en 10.102 casos aleatorios.',
        },
        {
          title: 'Bloqueos por servicio, conductor y bus',
          why: 'Dos despachadores pueden pulsar a la vez. Bloqueos consultivos tomados en orden fijo hacen que gane exactamente una asignación en conflicto por ráfaga: 60 de 480, con dos instancias de la API.',
        },
        {
          title: 'Una demo que duerme',
          why: 'La portada no pide sesión ni despierta la base de datos; esta solo despierta cuando alguien entra, y los datos se reconstruyen cada noche. El repositorio público se exporta por lista blanca, con comprobaciones antes de que salga nada.',
        },
      ],
      assistant: {
        title: 'Qué hizo la IA',
        why: 'Claude Code escribió la mayor parte del código, los tests y la documentación a partir de mis especificaciones y de los planes que aprobé. Revisé cada cambio, confirmé cada regla normativa y lanzo la publicación yo.',
      },
      limits: {
        title: 'Límites',
        why: 'Los tests demuestran que funcionan los casos que comprueban, no que no haya errores. La lectura del Reglamento es mía y no es asesoramiento legal. Las cifras se midieron en una máquina en la fecha que indican, y lo que no se pudo verificar está escrito en las notas de cierre de cada especificación.',
      },
    },
    code: {
      title: 'El código',
      pending:
        'El repositorio se publicará pronto, con todos los derechos reservados. Hasta entonces, lo que dice esta web se puede ver en la demo o se explica con su porqué.',
      link: 'Ver el código',
    },
  },
  engineering: {
    back: 'Portada',
    title: 'Cómo está hecho',
    intro:
      'Qué decisiones hay detrás, cómo se trabaja y qué salió mal. Lo que se afirma aquí se ve en la demo o se comprueba en el código público.',
    sections: [
      {
        id: 'arquitectura',
        title: 'Arquitectura',
        intro:
          'Un backend .NET 10 en capas (dominio, aplicación, infraestructura y API) y una web en React + TypeScript. Las dependencias van hacia dentro: el dominio no conoce la base de datos ni HTTP.',
        points: [
          {
            title: 'La ley vive en el dominio, sin base de datos',
            why: 'Las reglas de CE 561/2006 son cálculo puro sobre periodos de conducción. Así se prueban sin nada alrededor, y las cifras legales están en un único archivo.',
          },
          {
            title: 'Una sola imagen sirve la API y la web',
            why: 'Mismo origen: sin CORS y con la cookie de sesión siempre dentro. Es también lo que se arranca entero antes de publicar.',
          },
          {
            title: 'La base de datos duerme',
            why: 'La demo tiene pocas visitas y un coste mínimo. Por eso esta portada no le pregunta nada, la web espera a que despierte y los refrescos paran si nadie mira.',
          },
        ],
      },
      {
        id: 'decisiones',
        title: 'Decisiones y por qué',
        intro: 'Cada decisión importante tiene un registro (ADR) con lo que se descartó. Estas son las que más pesan.',
        points: [
          {
            title: 'Se valida antes de guardar, nunca después',
            why: 'Detectar una ilegalidad con el servicio ya en marcha es llegar tarde. El orden es fijo (conductor disponible, normativa, vehículo, solape) y al primer fallo no se guarda nada.',
          },
          {
            title: 'Forzar es posible, pero solo una regla y dejando rastro',
            why: 'A veces una persona tiene que decidir. Puede saltarse un incumplimiento de CE 561/2006 con motivo y nombre; el motor valida igual y el viaje queda marcado. Un solape o un vehículo no apto no se fuerzan.',
          },
          {
            title: 'Recomendar usa las mismas clases que asignar',
            why: 'Dos motores acabarían discrepando. La propuesta se simula sobre una copia en memoria con el mismo código, y al aplicarla cada línea se vuelve a validar.',
          },
          {
            title: 'Asignaciones simultáneas: bloqueo por servicio, conductor y vehículo',
            why: 'Leer, decidir y guardar tiene que ser una sola cosa. Los bloqueos son de PostgreSQL y se toman siempre en el mismo orden, así que no pueden cruzarse y valen con varias instancias.',
          },
          {
            title: 'La semana se cuenta en hora de Madrid y en UTC',
            why: 'El reglamento no dice en qué reloj cambia la semana. Deben cumplirse las dos, y se enseña la cifra más estricta.',
          },
        ],
      },
      {
        id: 'seguridad',
        title: 'Seguridad',
        intro: 'Sin inicio de sesión, por decisión: es una demo abierta. Eso obliga a cuidar el resto.',
        points: [
          {
            title: 'Sesión anónima firmada',
            why: 'Identifica un navegador, no una persona. Sin ella no se lee ni se escribe nada, y las escrituras llevan además una cabecera propia contra peticiones falsificadas.',
          },
          {
            title: 'Política de contenido estricta',
            why: 'La web solo carga lo suyo: ni scripts incrustados ni otros dominios. Se ve en las cabeceras de esta misma página.',
          },
          {
            title: 'Permisos mínimos',
            why: 'El contenedor no tiene consola ni gestor de paquetes y no corre como administrador. El usuario de la aplicación en la base de datos solo toca filas.',
          },
          {
            title: 'Límites de peticiones por sesión y por dirección',
            why: 'Un cálculo pesado repetido no puede dejar la demo sin servicio a los demás.',
          },
        ],
      },
      {
        id: 'proceso',
        title: 'Proceso, tal como es',
        intro:
          'Este proyecto se programa con un asistente de IA (Claude Code). Una persona decide qué se hace, aprueba cada plan y prueba el resultado; lo demás son comprobaciones que no dependen de la buena fe de nadie.',
        points: [
          {
            title: '1. SPEC',
            why: 'Un documento corto: qué, por qué, reglas y cómo se sabrá que está bien. Incluye lo que queda fuera.',
          },
          {
            title: '2. Plan revisado por una persona',
            why: 'Se aprueba, se corrige o se recorta antes de escribir código. Los textos de esta portada se aprobaron así.',
          },
          {
            title: '3. Primero los tests',
            why: 'En cada regla legal, los casos límite se escriben antes que la regla. Las normas las confirma la persona: el asistente no las «corrige».',
          },
          {
            title: '4. Puertas',
            why: 'No se cierra un cambio si no compila sin avisos y con los tests en verde. Lo que no se ha podido verificar se escribe en las notas de cierre.',
          },
        ],
      },
      {
        id: 'pruebas',
        title: 'Cómo se prueba y se publica',
        intro: 'Los tests con dobles no bastan: se aprendió por las malas.',
        points: [
          {
            title: 'PostgreSQL de verdad',
            why: 'Persistencia, concurrencia y permisos se prueban contra la misma versión de la base de datos, en un contenedor.',
          },
          {
            title: 'Un oráculo independiente',
            why: 'Las reglas semanales se comparan con otro cálculo escrito desde el texto legal, sin código del motor.',
          },
          {
            title: 'Publicar es un comando',
            why: 'Compila, pasa los tests de .NET y de la web, arranca la imagen como se va a desplegar, migra si hace falta, publica y comprueba lo publicado. Se detiene al primer fallo.',
          },
          {
            title: 'La demo pasa por la puerta de todos',
            why: 'Los datos se cargan cada noche por la API pública: si una asignación fuera ilegal, la carga fallaría.',
          },
        ],
      },
      {
        id: 'errores',
        title: 'Lo que salió mal',
        intro: 'Tres fallos reales y lo que cambió después.',
        points: [
          {
            title: 'Todo en verde y no funcionaba',
            why: 'En las primeras SPECs los tests usaban dobles. Casi todo el acceso a datos lanzaba «no implementado» y nadie lo vio. Desde entonces, lo que toca la base de datos o HTTP se prueba de verdad.',
          },
          {
            title: 'Dos personas a la vez',
            why: 'Con los tests de siempre en verde, se aceptaban todas las peticiones que se pisaban entre sí. Ahora leer, decidir y guardar ocurre con los recursos bloqueados, y hay tests que fuerzan el choque en el punto exacto.',
          },
          {
            title: 'Un test que no probaba la regla',
            why: 'El ejemplo del servicio que cruza el lunes pasaba sin la regla, y el oráculo usaba la misma librería de zonas horarias que el motor. Ahora cada parte de una regla de límites se rompe a propósito para ver que algún test falla.',
          },
        ],
      },
    ],
  },
  scene: {
    messages: [
      'Cargando la base de datos…',
      'Preparando las bases…',
      'Montando los horarios…',
      'Inventando excusas para las ausencias…',
    ],
    allLoaded: '¡Todo cargado!',
    waking: (wait, measuredOn) =>
      `La base de datos se apaga cuando nadie la usa y ahora está arrancando. La última vez tardó ${wait} (${measuredOn}); entrarás en cuanto esté lista.`,
    unavailable: 'La demo no está disponible ahora',
    timedOut: 'Han pasado dos minutos y la base de datos no ha respondido. Vuelve a intentarlo dentro de un rato.',
  },
  pageFailed: 'No se ha podido mostrar esta página.',
  appFailed: 'No se ha podido cargar la aplicación.',
  tryAgain: 'Volver a intentar',
};
