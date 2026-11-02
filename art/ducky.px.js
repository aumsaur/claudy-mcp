// Ducky: a rubber debugging duck. Skin for Claudy (Assets/claudy/skins/ducky/).
//
// Every sprite is drawn on 62x62 and outlined, so it exports at 64x64 like the
// other skins. Sprite names are the skin loader's file names (south, north,
// mood_happy, talk_0, walk_east_0, ...), so out/sprites/ maps 1:1 onto the skin
// folder. Anims whose names start with "preview_" only exist for the GIFs.
//
// Build (pxl skill, .claude/skills/pxl-pixel-art) to a folder outside Assets/,
// then copy its sprites/*.png into PetOverlay/Assets/claudy/skins/ducky/:
//   node .claude/skills/pxl-pixel-art/bin/pxl.js build art/ducky.px.js --out <dir>
// Never point --out inside Assets/: the csproj bundles every png under skins/,
// _preview/ included.

pal({
  k: "#2a1c24", // outline
  Y: "#c47a18", y: "#efb22e", l: "#fbd65a", L: "#fff1a8", // feathers, dark -> light
  O: "#a8461a", o: "#ec7a28", p: "#ffac5c", // beak and feet
  e: "#1b1420", w: "#ffffff", // eyes
  r: "#f4888a", R: "#d8506a", // blush, hearts
  m: "#5a1c2a", t: "#e8607a", // open mouth, tongue
  b: "#8fd3ff", // sweat
})
ramps("kYylL", "kOop")

const S = 62
const box = { w: S, h: S, outline: "k" }

// ---- shared bits -------------------------------------------------------------

sprite("_heart", `
  .RR.RR.
  RrwRrrR
  RrrrrrR
  .RrrrR.
  ..RrR..
  ...R...`)

sprite("_drop", `
  .b.
  .b.
  bwb
  bbb
  .b.`)

// little white motion ticks either side of a scared body
function shakeLines(c, dy = 0) {
  for (const [x, y] of [[4, 30], [3, 36], [4, 42]]) {
    c.hline(x, y + dy, 3, "w")
    c.hline(61 - x - 2, y + dy, 3, "w")
  }
}

// ---- front (south) -----------------------------------------------------------

function frontBody(c) {
  // body
  c.ellipse(31, 42, 18, 11, "y")
  c.ellipse(31, 46, 15, 7, "Y") // belly shadow
  c.ellipse(31, 44, 15, 7, "y")
  // wings tucked against the sides, tips pointing down
  c.ellipse(16, 42, 4, 7, "Y")
  c.ellipse(17, 41, 3, 6, "y")
  c.light([17, 38, 2], 1)
  // head
  c.ellipse(31, 36, 10, 1, "Y") // chin shadow on the body
  c.circ(31, 22, 13, "y")
  // tuft
  c.rect(30, 6, 2, 4, "y")
  c.px(29, 7, "y")
  c.px(28, 6, "y")
  // blush
  c.hline(19, 26, 3, "r")
}

const EYES = {
  normal(c) {
    c.rect(23, 18, 3, 5, "e")
    c.px(23, 18, "w")
    c.px(24, 19, "w")
  },
  happy(c) { // ^ ^
    c.line(21, 21, 24, 18, "e", 1)
    c.line(24, 18, 27, 21, "e", 1)
    c.line(22, 21, 24, 19, "e", 1)
    c.line(24, 19, 26, 21, "e", 1)
  },
  scared(c) { // wide, tiny pupils
    c.rect(21, 16, 6, 7, "e")
    c.rect(22, 17, 4, 5, "w")
    c.px(24, 19, "e")
  },
}

const BEAKS = {
  closed(c) {
    c.ellipse(31, 27, 7, 3, "o")
    c.hline(26, 26, 5, "p")
    c.hline(25, 28, 12, "O")
  },
  ajar(c) {
    c.ellipse(31, 27, 7, 3, "o")
    c.hline(26, 26, 5, "p")
    c.rect(26, 28, 10, 2, "m")
    c.hline(27, 31, 8, "O")
  },
  open(c) {
    c.ellipse(31, 26, 7, 3, "o")
    c.hline(26, 25, 5, "p")
    c.ellipse(31, 30, 5, 3, "m")
    c.rect(28, 31, 6, 2, "t")
    c.ellipse(31, 33, 6, 1, "o")
  },
  wobble(c) { // scared: a thin wavy gap
    c.ellipse(31, 27, 7, 3, "o")
    c.hline(26, 26, 5, "p")
    c.hline(25, 28, 3, "m")
    c.hline(28, 29, 3, "m")
    c.hline(31, 28, 2, "m")
  },
}

// The body and face are symmetric parts (draw the left half, mx mirrors it);
// feet, gloss and effects go on per sprite, since they aren't symmetric or move.
const FACES = [["normal", "closed"], ["normal", "ajar"], ["normal", "open"], ["happy", "open"], ["scared", "wobble"]]
for (const [eye, beak] of FACES)
  sprite(`_front_${eye}_${beak}`, { w: S, h: S, mx: true },
    (c) => { frontBody(c); EYES[eye](c); BEAKS[beak](c) })

// gloss on the upper left of the head, added after the mirror so there's one
function gloss(c, x = 0, y = 0) {
  c.light([25 + x, 15 + y, 4], 1)
  c.light([24 + x, 14 + y, 1], 1)
}

// lift > 0 raises the left foot, < 0 the right
function feet(c, lift = 0) {
  c.ellipse(24, 55 - Math.max(lift, 0), 5, 2, "o")
  c.ellipse(37, 55 - Math.max(-lift, 0), 5, 2, "o")
}

function front(c, face, { x = 0, y = 0, lift = 0 } = {}) {
  feet(c, lift)
  c.stamp("_front_" + face, x, y)
  gloss(c, x, y)
}

sprite("south", { ...box }, (c) => front(c, "normal_closed"))

// talk: beak closed -> ajar -> open -> ajar, at the app's 180ms talk tick
const TALK = ["closed", "ajar", "open", "ajar"]
TALK.forEach((b, i) => sprite("talk_" + i, { ...box }, (c) => front(c, "normal_" + b)))
anim("preview_talk", { ...box, frames: TALK.length, fps: 1000 / 180 },
  (c) => front(c, "normal_" + TALK[c.frame]))

sprite("mood_happy", { ...box },
  (c) => front(c, "happy_open"),
  (c) => { c.hline(18, 25, 5, "r"); c.hline(40, 25, 5, "r") },
  stamp("_heart", 47, 2), stamp("_heart", 7, 9))

sprite("mood_scared", { ...box },
  (c) => front(c, "scared_wobble"),
  (c) => shakeLines(c),
  stamp("_drop", 45, 11))

// ---- back (north) --------------------------------------------------------------

sprite("_back", { w: S, h: S, mx: true }, (c) => {
  c.ellipse(31, 42, 18, 11, "y")
  c.ellipse(31, 46, 15, 7, "Y")
  c.ellipse(31, 44, 15, 7, "y")
  c.ellipse(16, 42, 4, 7, "Y")
  c.ellipse(17, 41, 3, 6, "y")
  c.light([17, 38, 2], 1)
  c.ellipse(31, 36, 10, 1, "Y")
  c.circ(31, 22, 13, "y")
  c.rect(30, 6, 2, 4, "y")
  c.px(29, 7, "y")
  c.px(28, 6, "y")
  // the tail, flicking up from the middle of the back
  c.ellipse(31, 45, 8, 2, "Y") // its shadow on the body
  c.poly([24, 45, 31, 31, 38, 45], "Y")
  c.poly([25, 44, 31, 33, 37, 44], "l")
  c.poly([28, 43, 31, 36, 34, 43], "L")
})

function back(c, { x = 0, y = 0, lift = 0 } = {}) {
  feet(c, lift)
  c.stamp("_back", x, y)
  gloss(c, x, y)
}

sprite("north", { ...box }, (c) => back(c))

// ---- side (east, facing right) ---------------------------------------------------

function side(c, { dy = 0, front = 0, back = 0, liftFront = 0, liftBack = 0, tail = 0 } = {}) {
  // back foot, behind the body
  c.ellipse(24 + back, 55 - liftBack, 5, 2, "O")
  // tail
  c.poly([9, 40 + dy, 11 + tail, 29 + dy, 21, 35 + dy, 19, 45 + dy], "y")
  // body
  c.ellipse(29, 43 + dy, 18, 11, "y")
  c.ellipse(29, 47 + dy, 16, 7, "Y")
  c.ellipse(30, 45 + dy, 16, 7, "y")
  // wing
  c.ellipse(25, 42 + dy, 9, 6, "l")
  c.poly([14, 40 + dy, 20, 38 + dy, 18, 44 + dy], "l")
  c.ellipse(25, 47 + dy, 8, 1, "Y")
  c.light([21, 40 + dy, 2], 1)
  // front foot, in front of the body
  c.ellipse(36 + front, 55 - liftFront, 5, 2, "o")
  // head
  c.circ(37, 22 + dy, 12, "y")
  c.light([32, 15 + dy, 4], 1)
  c.light([31, 14 + dy, 1], 1)
  c.hline(28, 35 + dy, 14, "Y")
  // tuft, curling back
  c.rect(35, 7 + dy, 2, 4, "y")
  c.px(34, 6 + dy, "y")
  c.px(33, 6 + dy, "y")
  // beak
  c.ellipse(50, 26 + dy, 6, 3, "o")
  c.hline(46, 25 + dy, 6, "p")
  c.hline(46, 27 + dy, 10, "O")
  // eye and blush
  c.rect(41, 17 + dy, 3, 5, "e")
  c.px(42, 17 + dy, "w")
  c.px(43, 18 + dy, "w")
  c.hline(41, 25 + dy, 2, "r")
}

sprite("east", { ...box }, (c) => side(c))
sprite("west", { base: "east", flip: "x" })

// ---- walk cycles (the app flips walk frames every 90ms) ---------------------------

const WALK = 6
const wave = (i) => Math.sin((i / WALK) * Math.PI * 2)
const swing = (i) => Math.cos((i / WALK) * Math.PI * 2)

// side waddle: feet swap front/back, the foot swinging forward is lifted, the
// body rises as the feet pass each other, the tail wags
function sidePose(i) {
  const s = Math.round(wave(i) * 4)
  return {
    front: s,
    back: -s,
    liftFront: swing(i) > 0.3 ? 2 : 0,
    liftBack: swing(i) < -0.3 ? 2 : 0,
    dy: Math.abs(s) <= 2 ? -1 : 0,
    tail: i % 2 ? 1 : -1,
  }
}

// front/back waddle: rock toward the planted foot, lift the other one
function rockPose(i) {
  const s = wave(i)
  return { x: Math.round(s), y: Math.abs(s) < 0.5 ? -1 : 0, lift: s > 0.5 ? -2 : s < -0.5 ? 2 : 0 }
}

for (let i = 0; i < WALK; i++) {
  sprite("walk_east_" + i, { ...box }, (c) => side(c, sidePose(i)))
  sprite("walk_west_" + i, { base: "walk_east_" + i, flip: "x" })
  sprite("walk_south_" + i, { ...box }, (c) => front(c, "normal_closed", rockPose(i)))
  sprite("walk_north_" + i, { ...box }, (c) => back(c, rockPose(i)))
}

anim("preview_walk_east", { ...box, frames: WALK, fps: 1000 / 90 }, (c) => side(c, sidePose(c.frame)))
anim("preview_walk_south", { ...box, frames: WALK, fps: 1000 / 90 }, (c) => front(c, "normal_closed", rockPose(c.frame)))
anim("preview_walk_north", { ...box, frames: WALK, fps: 1000 / 90 }, (c) => back(c, rockPose(c.frame)))
