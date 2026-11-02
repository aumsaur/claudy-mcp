// Bolt: a two-wheeled helper robot with a screen face. Skin for Claudy
// (Assets/claudy/skins/bolt/).
//
// Same contract as ducky.px.js: 62x62 + outline = 64x64, sprite names are the
// skin loader's file names, "preview_" anims only exist for the GIFs.
//
// Build (pxl skill, .claude/skills/pxl-pixel-art) to a folder outside Assets/,
// then copy its sprites/*.png into PetOverlay/Assets/claudy/skins/bolt/:
//   node .claude/skills/pxl-pixel-art/bin/pxl.js build art/bolt.px.js --out <dir>
// Never point --out inside Assets/: the csproj bundles every png under skins/,
// _preview/ included.

pal({
  k: "#1c1b2e", // outline
  M: "#4f5d7a", m: "#8b9dbb", s: "#c3d0e4", W: "#eef3fb", // metal, dark -> light
  A: "#b8552e", a: "#e8834f", // clay-orange trim
  d: "#121828", D: "#25324e", // screen
  C: "#2aa9b8", c: "#62f0e2", // screen glyphs
  t: "#2b2a38", T: "#4c4b5e", // tyre
  g: "#ffd25a", y: "#fff3b8", // antenna bulb
  G: "#8a8270", // bulb, dimmed
  r: "#ff7a9a", R: "#d8506a", // cheek LEDs, hearts
  b: "#8fd3ff", // sweat
})
ramps("kMmsW", "kAa", "dD", "tT")

const S = 62
const box = { w: S, h: S, outline: "k" }

function rrect(c, x, y, w, h, r, col) {
  c.rect(x + r, y, w - 2 * r, h, col)
  c.rect(x, y + r, w, h - 2 * r, col)
  c.circ(x + r, y + r, r, col)
  c.circ(x + w - 1 - r, y + r, r, col)
  c.circ(x + r, y + h - 1 - r, r, col)
  c.circ(x + w - 1 - r, y + h - 1 - r, r, col)
}

// ---- shared bits -------------------------------------------------------------

// antenna bulbs, 5x5 so they read round
sprite("_bulb", `
  .ggg.
  gyggg
  ggggg
  ggggg
  .ggg.`)
sprite("_bulb_dim", { base: "_bulb", swap: { g: "G", y: "s" } })
sprite("_bulb_heart", `
  RR.RR
  RrRrR
  RrrrR
  .RrR.
  ..R..`)

sprite("_heart", `
  .RR.RR.
  RrWRrrR
  RrrrrrR
  .RrrrR.
  ..RrR..
  ...R...`)

sprite("_drop", `
  .b.
  .b.
  bWb
  bbb
  .b.`)

function shakeLines(c) {
  for (const [x, y] of [[4, 22], [3, 28], [4, 34]]) {
    c.hline(x, y, 3, "W")
    c.hline(61 - x - 2, y, 3, "W")
  }
}

// screen glyphs and the bulb light up their surroundings a little
const shine = () => [glow("c", { r: 2, strength: 0.35 }), glow("g", { r: 2, strength: 0.25 })]

// ---- front (south) and back (north): symmetric halves, mx mirrors them ----------

// two wheels on an axle, seen edge-on; tread lines scroll down as it rolls
function wheels(c, tread = 0) {
  c.rect(20, 49, 11, 3, "M")
  rrect(c, 15, 43, 8, 15, 3, "t")
  for (let y = 45 + (tread % 3); y <= 55; y += 3) c.hline(16, y, 6, "T")
  c.vline(22, 46, 9, "T")
}

function shell(c, dy) {
  c.ellipse(31, 45 + dy, 10, 3, "M")
  c.rect(30, 8 + dy, 2, 8, "M") // antenna stalk
  // ear bolts
  c.rect(10, 24 + dy, 4, 9, "A")
  c.rect(10, 25 + dy, 2, 7, "a")
  // body
  rrect(c, 13, 14 + dy, 36, 31, 5, "m")
  c.rect(15, 40 + dy, 32, 3, "M") // lower band
  c.hline(18, 15 + dy, 26, "s")
  c.hline(16, 37 + dy, 30, "A") // trim stripe
  c.hline(16, 38 + dy, 30, "a")
}

function screen(c, dy) {
  rrect(c, 17, 18 + dy, 28, 18, 3, "D")
  rrect(c, 18, 19 + dy, 26, 16, 3, "d")
}

const EYES = {
  normal(c, dy) {
    c.rect(23, 23 + dy, 3, 6, "c")
    c.px(23, 23 + dy, "W")
  },
  happy(c, dy) { // ^ ^
    c.line(21, 27 + dy, 24, 24 + dy, "c")
    c.line(24, 24 + dy, 27, 27 + dy, "c")
    c.line(21, 28 + dy, 24, 25 + dy, "c")
    c.line(24, 25 + dy, 27, 28 + dy, "c")
  },
  scared(c, dy) { // hollow O O
    c.box(21, 22 + dy, 6, 7, "c")
    c.px(21, 22 + dy, "d")
    c.px(21, 28 + dy, "d")
    c.px(24, 25 + dy, "C")
  },
}

const MOUTHS = {
  rest: [0, 0, 0],
  low: [1, 2, 1],
  mid: [2, 1, 3],
  high: [1, 3, 2],
}
function mouth(c, kind, dy) {
  if (kind === "smile") {
    c.px(25, 31 + dy, "c")
    c.hline(26, 32 + dy, 5, "c")
    return
  }
  if (kind === "zigzag") {
    for (let x = 25; x <= 30; x++) c.px(x, 32 + dy - (x % 2), "c")
    return
  }
  // equalizer: three bar pairs, heights per frame (0 = flat line)
  MOUTHS[kind].forEach((h, i) => {
    const x = 26 + i * 2
    if (h === 0) c.hline(x, 32 + dy, 2, "C")
    else c.rect(x, 32 + dy - h, 1, 2 * h + 1, "c")
  })
}

const FACES = [
  ["normal", "rest"], ["normal", "low"], ["normal", "mid"], ["normal", "high"],
  ["happy", "smile"], ["scared", "zigzag"],
]
for (const [eye, m] of FACES)
  for (const dy of [0, -1])
    sprite(`_front_${eye}_${m}_${-dy}`, { w: S, h: S, mx: true }, (c) => {
      shell(c, dy)
      screen(c, dy)
      EYES[eye](c, dy)
      mouth(c, m, dy)
      c.hline(20, 31 + dy, eye === "happy" ? 3 : 2, "r") // cheek LEDs
    })

for (const dy of [0, -1])
  sprite(`_back_${-dy}`, { w: S, h: S, mx: true }, (c) => {
    shell(c, dy)
    // service hatch with vent slats and corner screws
    rrect(c, 18, 19 + dy, 26, 15, 2, "M")
    for (let y = 22; y <= 30; y += 3) c.hline(21, y + dy, 20, "t")
    c.px(20, 21 + dy, "s")
    c.px(20, 31 + dy, "s")
  })

// screen glare and body shine, after the mirror so there's one of each
function frontGloss(c, dy) {
  c.line(20, 22 + dy, 22, 20 + dy, "D")
  c.px(19, 23 + dy, "D")
  c.light([16, 17 + dy, 2], 1)
}

function front(c, face, { bulb = "_bulb", lift = 0, tread = 0 } = {}) {
  wheels(c, tread)
  c.stamp(`_front_${face}_${lift}`, 0, 0)
  c.stamp(bulb, 29, 3 - lift)
  frontGloss(c, -lift)
}

function back(c, { lift = 0, tread = 0 } = {}) {
  wheels(c, tread)
  c.stamp(`_back_${lift}`, 0, 0)
  c.stamp("_bulb", 29, 3 - lift)
  c.light([45, 17 - lift, 2], 1) // light still comes from the upper left of the screen
}

// mx can't mirror the wheels' tread phase (they share it), so wheels() draws the
// left wheel and this copies it across
function mirrorWheels(c) {
  for (let y = 43; y < 58; y++)
    for (let x = 15; x < 23; x++) {
      const k = c.key(x, y)
      if (k) c.px(61 - x, y, k)
    }
}

sprite("south", { ...box }, (c) => { front(c, "normal_rest"); mirrorWheels(c) }, ...shine())
sprite("north", { ...box }, (c) => { back(c); mirrorWheels(c) }, ...shine())

// talk: the mouth is a little level meter, at the app's 180ms talk tick
const TALK = ["low", "high", "mid", "high", "low", "mid"]
TALK.forEach((m, i) =>
  sprite("talk_" + i, { ...box }, (c) => { front(c, "normal_" + m); mirrorWheels(c) }, ...shine()))
anim("preview_talk", { ...box, frames: TALK.length, fps: 1000 / 180 },
  (c) => { front(c, "normal_" + TALK[c.frame]); mirrorWheels(c) }, ...shine())

sprite("mood_happy", { ...box },
  (c) => { front(c, "happy_smile", { bulb: "_bulb_heart" }); mirrorWheels(c) },
  stamp("_heart", 48, 4), stamp("_heart", 7, 8), ...shine())

sprite("mood_scared", { ...box },
  (c) => { front(c, "scared_zigzag", { bulb: "_bulb_dim" }); mirrorWheels(c) },
  (c) => shakeLines(c),
  stamp("_drop", 50, 13), ...shine())

// ---- side (east, facing right) ---------------------------------------------------

function side(c, { dy = 0, spin = 0, lean = 0 } = {}) {
  // antenna, leaning back while it rolls
  c.line(28, 15 + dy, 27 + lean, 8 + dy, "M", 2)
  c.stamp("_bulb", 25 + lean, 3 + dy)
  // wheel: tyre with tread blocks and a three-spoke hub, all turned by spin
  c.circ(30, 50, 7, "t")
  for (let k = 0; k < 6; k++) {
    const a = spin + (k * Math.PI) / 3
    c.px(30 + Math.round(Math.cos(a) * 6), 50 + Math.round(Math.sin(a) * 6), "T")
  }
  c.circ(30, 50, 4, "s")
  for (let k = 0; k < 3; k++) {
    const a = spin + (k * Math.PI * 2) / 3
    c.line(30, 50, 30 + Math.round(Math.cos(a) * 4), 50 + Math.round(Math.sin(a) * 4), "M")
  }
  c.px(30, 50, "A")
  // mudguard: the top half of two rings around the wheel
  c.clip([19, 40, 24, 10], () => { c.ring(30, 50, 9, "M"); c.ring(30, 50, 8, "m") })
  // body
  rrect(c, 16, 14 + dy, 29, 31, 5, "m")
  c.rect(18, 40 + dy, 25, 3, "M")
  c.hline(20, 15 + dy, 20, "s")
  c.hline(17, 37 + dy, 27, "A")
  c.hline(17, 38 + dy, 27, "a")
  // ear bolt in profile
  c.circ(26, 27 + dy, 4, "A")
  c.circ(26, 27 + dy, 2, "a")
  c.px(25, 26 + dy, "W")
  // visor face, front edge of the head
  rrect(c, 35, 18 + dy, 12, 18, 3, "D")
  rrect(c, 36, 19 + dy, 10, 16, 3, "d")
  c.rect(41, 23 + dy, 3, 6, "c")
  c.px(41, 23 + dy, "W")
  c.hline(41, 32 + dy, 3, "C")
  c.px(39, 31 + dy, "r")
  c.line(37, 21 + dy, 38, 20 + dy, "D")
  c.light([19, 17 + dy, 2], 1)
}

sprite("east", { ...box }, (c) => side(c), ...shine())
sprite("west", { base: "east", flip: "x" })

// ---- walk cycles (the app flips walk frames every 90ms) ---------------------------

// side: the wheel turns clockwise (heading right), the body hums on its
// suspension, the antenna trails behind. Front/back: the treads scroll.
const WALK = 6
const rollPose = (i) => ({ spin: (i / WALK) * ((Math.PI * 2) / 3), dy: i % 3 === 1 ? -1 : 0, lean: i % 2 ? -2 : -1 })
const facePose = (i) => ({ tread: i, lift: i % 3 === 1 ? 1 : 0 })

for (let i = 0; i < WALK; i++) {
  sprite("walk_east_" + i, { ...box }, (c) => side(c, rollPose(i)), ...shine())
  sprite("walk_west_" + i, { base: "walk_east_" + i, flip: "x" })
  sprite("walk_south_" + i, { ...box }, (c) => { front(c, "normal_rest", facePose(i)); mirrorWheels(c) }, ...shine())
  sprite("walk_north_" + i, { ...box }, (c) => { back(c, facePose(i)); mirrorWheels(c) }, ...shine())
}

anim("preview_walk_east", { ...box, frames: WALK, fps: 1000 / 90 }, (c) => side(c, rollPose(c.frame)), ...shine())
anim("preview_walk_south", { ...box, frames: WALK, fps: 1000 / 90 },
  (c) => { front(c, "normal_rest", facePose(c.frame)); mirrorWheels(c) }, ...shine())
