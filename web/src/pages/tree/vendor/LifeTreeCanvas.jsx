import { useEffect, useRef } from "react";
import * as THREE from "three";
import { categoryById, RECOGNITION_CATEGORIES } from "./categories.js";

const BG_TOP = "#efe9e2";
const BG_MID = "#f4efe8";
const BG_BOTTOM = "#ece5da";
const FOG_COLOR = 0xf2ede5;

function hashSeed(text) {
  let h = 2166136261;
  for (let i = 0; i < text.length; i += 1) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function mulberry32(seed) {
  let t = seed >>> 0;
  return () => {
    t += 0x6d2b79f5;
    let r = Math.imul(t ^ (t >>> 15), 1 | t);
    r ^= r + Math.imul(r ^ (r >>> 7), 61 | r);
    return ((r ^ (r >>> 14)) >>> 0) / 4294967296;
  };
}

function makeCanvas(width, height = width) {
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  return { canvas, ctx: canvas.getContext("2d") };
}

function toTexture(canvas, { repeatX = 1, repeatY = 1, wrap = false } = {}) {
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 4;
  if (wrap) {
    texture.wrapS = THREE.RepeatWrapping;
    texture.wrapT = THREE.RepeatWrapping;
    texture.repeat.set(repeatX, repeatY);
  }
  return texture;
}

/** Pale ivory bark with soft vertical grain, matching the light dreamy tree. */
function createBarkTexture() {
  const size = 256;
  const { canvas, ctx } = makeCanvas(size);
  const base = ctx.createLinearGradient(0, 0, size, 0);
  base.addColorStop(0, "#cbb89b");
  base.addColorStop(0.3, "#e9dcc4");
  base.addColorStop(0.55, "#ddcdb0");
  base.addColorStop(0.8, "#efe3cd");
  base.addColorStop(1, "#c6b294");
  ctx.fillStyle = base;
  ctx.fillRect(0, 0, size, size);

  const rand = mulberry32(0xba12);
  for (let i = 0; i < 40; i += 1) {
    const x = rand() * size;
    const wobble = (rand() - 0.5) * 9;
    ctx.strokeStyle = `rgba(${150 + rand() * 30}, ${132 + rand() * 26}, ${104 + rand() * 22}, ${0.12 + rand() * 0.18})`;
    ctx.lineWidth = 1 + rand() * 2;
    ctx.beginPath();
    ctx.moveTo(x, 0);
    for (let y = 0; y <= size; y += 8) {
      ctx.lineTo(x + Math.sin(y * 0.08 + i) * wobble, y);
    }
    ctx.stroke();
  }

  for (let i = 0; i < 140; i += 1) {
    ctx.fillStyle = `rgba(255, 251, 240, ${0.05 + rand() * 0.08})`;
    ctx.fillRect(rand() * size, rand() * size, 1 + rand() * 2, 4 + rand() * 14);
  }

  return toTexture(canvas, { wrap: true, repeatX: 2, repeatY: 3 });
}

function createBarkBumpTexture() {
  const size = 256;
  const { canvas, ctx } = makeCanvas(size);
  ctx.fillStyle = "#808080";
  ctx.fillRect(0, 0, size, size);
  const rand = mulberry32(0xb04);
  for (let i = 0; i < 44; i += 1) {
    const x = rand() * size;
    ctx.strokeStyle = `rgba(${100 + rand() * 40}, ${100 + rand() * 40}, ${100 + rand() * 40}, 0.4)`;
    ctx.lineWidth = 1 + rand() * 2;
    ctx.beginPath();
    ctx.moveTo(x, 0);
    for (let y = 0; y <= size; y += 6) {
      ctx.lineTo(x + Math.sin(y * 0.1 + i) * 4, y);
    }
    ctx.stroke();
  }
  return toTexture(canvas, { wrap: true, repeatX: 2, repeatY: 3 });
}

/** Warm cream studio backdrop with a soft glow behind the tree. */
function createBackdropTexture() {
  const size = 512;
  const { canvas, ctx } = makeCanvas(size);
  const sky = ctx.createLinearGradient(0, 0, 0, size);
  sky.addColorStop(0, BG_TOP);
  sky.addColorStop(0.45, BG_MID);
  sky.addColorStop(1, BG_BOTTOM);
  ctx.fillStyle = sky;
  ctx.fillRect(0, 0, size, size);

  const glow = ctx.createRadialGradient(size * 0.5, size * 0.46, 20, size * 0.5, size * 0.46, size * 0.5);
  glow.addColorStop(0, "rgba(255, 251, 242, 0.65)");
  glow.addColorStop(0.55, "rgba(255, 248, 236, 0.22)");
  glow.addColorStop(1, "rgba(255, 248, 236, 0)");
  ctx.fillStyle = glow;
  ctx.fillRect(0, 0, size, size);

  return toTexture(canvas);
}

/** Lush mossy grass for the round base. */
function createGroundTexture() {
  const size = 256;
  const { canvas, ctx } = makeCanvas(size);
  const grass = ctx.createRadialGradient(size * 0.5, size * 0.5, 10, size * 0.5, size * 0.5, size * 0.55);
  grass.addColorStop(0, "#a4c07e");
  grass.addColorStop(0.55, "#8bab66");
  grass.addColorStop(1, "#77995a");
  ctx.fillStyle = grass;
  ctx.fillRect(0, 0, size, size);

  const rand = mulberry32(0x62a55);
  for (let i = 0; i < 1100; i += 1) {
    ctx.fillStyle = `rgba(${70 + rand() * 60}, ${110 + rand() * 60}, ${45 + rand() * 45}, ${0.06 + rand() * 0.14})`;
    ctx.fillRect(rand() * size, rand() * size, 1, 1 + rand() * 2);
  }
  for (let i = 0; i < 90; i += 1) {
    ctx.fillStyle = `rgba(${210 + rand() * 40}, ${220 + rand() * 30}, ${170 + rand() * 40}, ${0.08 + rand() * 0.12})`;
    ctx.fillRect(rand() * size, rand() * size, 1, 1 + rand() * 2);
  }
  return toTexture(canvas);
}

function createLeafTexture() {
  const size = 128;
  const { canvas, ctx } = makeCanvas(size);
  const gradient = ctx.createRadialGradient(size * 0.42, size * 0.38, 4, size * 0.5, size * 0.5, size * 0.48);
  gradient.addColorStop(0, "rgba(255,255,255,0.95)");
  gradient.addColorStop(0.45, "rgba(255,255,255,0.55)");
  gradient.addColorStop(1, "rgba(255,255,255,0)");
  ctx.fillStyle = gradient;
  ctx.beginPath();
  ctx.ellipse(size * 0.5, size * 0.52, size * 0.38, size * 0.48, -0.35, 0, Math.PI * 2);
  ctx.fill();
  return toTexture(canvas);
}

function createGrassTexture() {
  const size = 64;
  const { canvas, ctx } = makeCanvas(size);
  ctx.clearRect(0, 0, size, size);
  const rand = mulberry32(0x921);
  for (let i = 0; i < 5; i += 1) {
    const x = 18 + i * 8 + (rand() - 0.5) * 4;
    const grad = ctx.createLinearGradient(x, size, x, 8);
    grad.addColorStop(0, "rgba(88, 128, 66, 0)");
    grad.addColorStop(0.2, "rgba(88, 128, 66, 0.8)");
    grad.addColorStop(1, "rgba(166, 204, 118, 0.2)");
    ctx.strokeStyle = grad;
    ctx.lineWidth = 1.4 + rand();
    ctx.beginPath();
    ctx.moveTo(x, size - 2);
    ctx.quadraticCurveTo(x + (rand() - 0.5) * 10, size * 0.45, x + (rand() - 0.5) * 6, 10 + rand() * 8);
    ctx.stroke();
  }
  return toTexture(canvas);
}

function createFlowerTexture(petalColor, centerColor) {
  const size = 64;
  const { canvas, ctx } = makeCanvas(size);
  ctx.clearRect(0, 0, size, size);
  const cx = size * 0.5;
  const cy = size * 0.55;
  for (let i = 0; i < 5; i += 1) {
    const angle = (i / 5) * Math.PI * 2 - Math.PI / 2;
    const px = cx + Math.cos(angle) * 10;
    const py = cy + Math.sin(angle) * 10;
    const petal = ctx.createRadialGradient(px, py, 1, px, py, 11);
    petal.addColorStop(0, petalColor);
    petal.addColorStop(1, "rgba(255,255,255,0)");
    ctx.fillStyle = petal;
    ctx.beginPath();
    ctx.ellipse(px, py, 9, 7, angle, 0, Math.PI * 2);
    ctx.fill();
  }
  const center = ctx.createRadialGradient(cx, cy, 1, cx, cy, 6);
  center.addColorStop(0, centerColor);
  center.addColorStop(1, "rgba(255,255,255,0)");
  ctx.fillStyle = center;
  ctx.beginPath();
  ctx.arc(cx, cy, 6, 0, Math.PI * 2);
  ctx.fill();
  return toTexture(canvas);
}

function branchAnchors() {
  return RECOGNITION_CATEGORIES.map((category, index) => {
    const angle = (index / RECOGNITION_CATEGORIES.length) * Math.PI * 2 - Math.PI / 2;
    const height = 1.15 + (index % 3) * 0.5 + (index % 2) * 0.16;
    const radius = 0.95 + (index % 4) * 0.12;
    return {
      categoryId: category.id,
      origin: new THREE.Vector3(0, height * 0.52, 0),
      tip: new THREE.Vector3(Math.cos(angle) * radius, height, Math.sin(angle) * radius * 0.88),
    };
  });
}

function buildTreeStructure(group, barkMap, barkBump) {
  const woodMat = new THREE.MeshStandardMaterial({
    map: barkMap,
    bumpMap: barkBump,
    bumpScale: 0.035,
    color: 0xffffff,
    roughness: 0.78,
    metalness: 0.02,
  });

  const trunkGeo = new THREE.CylinderGeometry(0.09, 0.185, 2.2, 16, 6);
  const trunk = new THREE.Mesh(trunkGeo, woodMat);
  trunk.position.y = 0.98;
  group.add(trunk);

  const rootGeo = new THREE.CylinderGeometry(0.21, 0.36, 0.18, 14);
  const root = new THREE.Mesh(rootGeo, woodMat);
  root.position.y = 0.05;
  group.add(root);

  // Surface roots
  for (let i = 0; i < 5; i += 1) {
    const angle = (i / 5) * Math.PI * 2 + 0.2;
    const len = 0.3 + (i % 2) * 0.08;
    const rootArm = new THREE.Mesh(
      new THREE.CylinderGeometry(0.018, 0.05, len, 6),
      woodMat,
    );
    rootArm.position.set(Math.cos(angle) * 0.17, 0.03, Math.sin(angle) * 0.17);
    rootArm.rotation.z = Math.PI / 2.4;
    rootArm.rotation.y = -angle;
    group.add(rootArm);
  }

  const branchMat = new THREE.MeshStandardMaterial({
    map: barkMap,
    bumpMap: barkBump,
    bumpScale: 0.025,
    color: 0xf3e8d4,
    roughness: 0.82,
    metalness: 0.02,
  });

  const anchors = branchAnchors();
  for (const anchor of anchors) {
    const direction = new THREE.Vector3().subVectors(anchor.tip, anchor.origin);
    const length = direction.length();
    const mid = new THREE.Vector3().addVectors(anchor.origin, anchor.tip).multiplyScalar(0.5);
    const branch = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.045, length, 7), branchMat);
    branch.position.copy(mid);
    branch.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.clone().normalize());
    group.add(branch);

    for (let i = 0; i < 2; i += 1) {
      const t = 0.48 + i * 0.26;
      const base = new THREE.Vector3().lerpVectors(anchor.origin, anchor.tip, t);
      const side = new THREE.Vector3(-direction.z, 0.18, direction.x).normalize().multiplyScalar(0.28 + i * 0.07);
      if (i % 2) side.negate();
      const tip = base.clone().add(side).add(new THREE.Vector3(0, 0.18, 0));
      const twigDir = new THREE.Vector3().subVectors(tip, base);
      const twigLen = twigDir.length();
      const twigMid = base.clone().add(tip).multiplyScalar(0.5);
      const twig = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.018, twigLen, 5), branchMat);
      twig.position.copy(twigMid);
      twig.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), twigDir.normalize());
      group.add(twig);
    }
  }
  return anchors;
}

/** Sparse soft green filler foliage along branches — decoration only. */
function scatterFoliage(group, anchors, leafTexture) {
  const palette = ["#b3c98a", "#a5c07c", "#c2d49a", "#9cb873"];
  const rand = mulberry32(0x1eaf);
  const materials = palette.map(
    (color) =>
      new THREE.MeshStandardMaterial({
        color: new THREE.Color(color),
        map: leafTexture,
        transparent: true,
        opacity: 0.85,
        roughness: 0.55,
        depthWrite: false,
        side: THREE.DoubleSide,
      }),
  );

  for (const anchor of anchors) {
    const count = 4 + Math.floor(rand() * 3);
    for (let i = 0; i < count; i += 1) {
      const t = 0.22 + rand() * 0.6;
      const base = new THREE.Vector3().lerpVectors(anchor.origin, anchor.tip, t);
      const outward = new THREE.Vector3().subVectors(anchor.tip, anchor.origin).normalize();
      const side = new THREE.Vector3(-outward.z, 0, outward.x).normalize();
      base
        .add(side.multiplyScalar((rand() - 0.5) * 0.3))
        .add(new THREE.Vector3(0, (rand() - 0.35) * 0.24, 0));
      const leaf = new THREE.Mesh(
        new THREE.PlaneGeometry(0.09 + rand() * 0.05, 0.12 + rand() * 0.07),
        materials[Math.floor(rand() * materials.length)],
      );
      leaf.position.copy(base);
      leaf.rotation.set(rand() * 0.8 - 0.4, rand() * Math.PI * 2, rand() * 0.9 - 0.45);
      group.add(leaf);
    }
  }
}

/** One category-tinted leaf per recognition — the interactive layer. */
function placeLeaves(recognitions, anchors, leafTexture) {
  const leafGroup = new THREE.Group();
  const leafMeshes = [];
  const byCategory = Object.fromEntries(anchors.map((a) => [a.categoryId, []]));

  for (const item of recognitions) {
    (byCategory[item.category] ??= []).push(item);
  }

  for (const anchor of anchors) {
    const items = byCategory[anchor.categoryId] ?? [];
    items.forEach((item, index) => {
      const rand = mulberry32(hashSeed(item.id));
      const along = 0.42 + (index / Math.max(items.length, 1)) * 0.5 + rand() * 0.08;
      const base = new THREE.Vector3().lerpVectors(anchor.origin, anchor.tip, Math.min(along, 0.98));
      const outward = new THREE.Vector3().subVectors(anchor.tip, anchor.origin).normalize();
      const side = new THREE.Vector3(-outward.z, 0, outward.x).normalize();
      const up = new THREE.Vector3(0, 1, 0);
      const offset = side
        .multiplyScalar((rand() - 0.5) * 0.62)
        .add(up.multiplyScalar((rand() - 0.25) * 0.45))
        .add(outward.multiplyScalar((rand() - 0.5) * 0.16));

      const color = categoryById[item.category]?.color ?? "#7a8a7a";
      const material = new THREE.MeshStandardMaterial({
        color: new THREE.Color(color),
        map: leafTexture,
        transparent: true,
        opacity: 0.92,
        roughness: 0.52,
        metalness: 0.04,
        depthWrite: false,
        side: THREE.DoubleSide,
      });
      const leaf = new THREE.Mesh(
        new THREE.PlaneGeometry(0.24 + rand() * 0.08, 0.32 + rand() * 0.1),
        material,
      );
      leaf.position.copy(base.add(offset));
      leaf.rotation.set(rand() * 0.8 - 0.4, rand() * Math.PI * 2, rand() * 0.9 - 0.45);
      leaf.userData = {
        id: item.id,
        category: item.category,
        phase: rand() * Math.PI * 2,
        amp: 0.03 + rand() * 0.04,
        basePosition: leaf.position.clone(),
        baseRotationZ: leaf.rotation.z,
      };
      leafGroup.add(leaf);
      leafMeshes.push(leaf);
    });
  }

  return { leafGroup, leafMeshes };
}

/** Round grassy base on a white platter, replacing the open meadow. */
function buildBase(scene, groundTexture) {
  const base = new THREE.Group();

  const platter = new THREE.Mesh(
    new THREE.CylinderGeometry(2.0, 2.06, 0.14, 64),
    new THREE.MeshStandardMaterial({ color: 0xf8f4ec, roughness: 0.55, metalness: 0.05 }),
  );
  platter.position.y = -0.25;
  base.add(platter);

  const mound = new THREE.Mesh(
    new THREE.CylinderGeometry(1.72, 1.88, 0.2, 48),
    new THREE.MeshStandardMaterial({ color: 0x87a862, roughness: 0.95 }),
  );
  mound.position.y = -0.1;
  base.add(mound);

  const grassTop = new THREE.Mesh(
    new THREE.CircleGeometry(1.72, 48),
    new THREE.MeshStandardMaterial({ map: groundTexture, roughness: 0.95 }),
  );
  grassTop.rotation.x = -Math.PI / 2;
  grassTop.position.y = 0.002;
  base.add(grassTop);

  const shadow = new THREE.Mesh(
    new THREE.CircleGeometry(0.75, 32),
    new THREE.MeshBasicMaterial({ color: 0x4e6339, transparent: true, opacity: 0.18, depthWrite: false }),
  );
  shadow.rotation.x = -Math.PI / 2;
  shadow.position.y = 0.008;
  base.add(shadow);

  scene.add(base);
  return base;
}

function scatterGroundFlora(parent, grassMap, flowerMaps) {
  const flora = new THREE.Group();
  const rand = mulberry32(0xf10a);
  const grassMat = new THREE.MeshStandardMaterial({
    map: grassMap,
    transparent: true,
    depthWrite: false,
    side: THREE.DoubleSide,
    roughness: 0.85,
  });

  for (let i = 0; i < 60; i += 1) {
    const angle = rand() * Math.PI * 2;
    const radius = 0.42 + rand() * 1.22;
    const blade = new THREE.Mesh(new THREE.PlaneGeometry(0.18 + rand() * 0.12, 0.22 + rand() * 0.18), grassMat);
    blade.position.set(Math.cos(angle) * radius, 0.1 + rand() * 0.04, Math.sin(angle) * radius);
    blade.rotation.y = rand() * Math.PI;
    blade.rotation.x = -0.12 + rand() * 0.2;
    flora.add(blade);
    const blade2 = blade.clone();
    blade2.rotation.y += Math.PI / 2;
    flora.add(blade2);
  }

  flowerMaps.forEach((map, flowerIndex) => {
    const mat = new THREE.MeshStandardMaterial({
      map,
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      roughness: 0.6,
      metalness: 0.02,
      emissive: new THREE.Color("#fff2d8"),
      emissiveIntensity: 0.08,
    });
    const count = 9 + flowerIndex * 2;
    for (let i = 0; i < count; i += 1) {
      const angle = rand() * Math.PI * 2;
      const radius = 0.55 + rand() * 1.1;
      const flower = new THREE.Mesh(new THREE.PlaneGeometry(0.12 + rand() * 0.06, 0.12 + rand() * 0.06), mat);
      flower.position.set(Math.cos(angle) * radius, 0.07 + rand() * 0.03, Math.sin(angle) * radius);
      flower.rotation.y = rand() * Math.PI;
      flower.userData = { sway: rand() * Math.PI * 2, kind: "flower" };
      flora.add(flower);
    }
  });

  parent.add(flora);
  return flora;
}

function lerpAngle(current, target, amount) {
  let diff = target - current;
  while (diff > Math.PI) diff -= Math.PI * 2;
  while (diff < -Math.PI) diff += Math.PI * 2;
  return current + diff * amount;
}

function rotationToFaceLeaf(leafLocal, camera) {
  const leafAngle = Math.atan2(leafLocal.x, leafLocal.z);
  const cameraAngle = Math.atan2(camera.position.x, camera.position.z);
  return cameraAngle - leafAngle;
}

const LOOK_AT = new THREE.Vector3(0, 1.25, 0);
const DEFAULT_DISTANCE = 8.2;
const MIN_DISTANCE = 4.0;
const MAX_DISTANCE = 14.5;

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

/** Place camera along a fixed viewing direction at `distance` from the tree. */
function applyCameraDistance(camera, host, distance) {
  const tall = host.clientHeight > host.clientWidth * 1.05;
  camera.fov = tall ? 30 : 34;
  const elevation = tall ? 0.18 : 0.22;
  const azimuth = 0.025;
  const horizontal = Math.cos(elevation) * distance;
  camera.position.set(
    LOOK_AT.x + Math.sin(azimuth) * horizontal,
    LOOK_AT.y + Math.sin(elevation) * distance,
    LOOK_AT.z + Math.cos(azimuth) * horizontal,
  );
  camera.lookAt(LOOK_AT);
  camera.updateProjectionMatrix();
}

/**
 * Procedural Three.js life tree in a soft dreamy style: pale ivory tree on a
 * round grassy platter. Recognitions render as category-tinted leaves;
 * hovering a leaf draws a connector line to a time + title callout.
 */
export function LifeTreeCanvas({
  recognitions,
  activeCategory = "all",
  selectedId = null,
  focusKey = 0,
  onSelect,
}) {
  const hostRef = useRef(null);
  const svgRef = useRef(null);
  const lineRef = useRef(null);
  const dotRef = useRef(null);
  const calloutRef = useRef(null);
  const calloutTimeRef = useRef(null);
  const calloutTitleRef = useRef(null);
  const stateRef = useRef({
    leafMeshes: [],
    activeCategory: "all",
    selectedId: null,
    hoveredId: null,
    focusKey: 0,
    onSelect: null,
    rotationY: 0,
    targetRotationY: 0,
    dragging: false,
  });

  stateRef.current.activeCategory = activeCategory;
  stateRef.current.selectedId = selectedId;
  stateRef.current.focusKey = focusKey;
  stateRef.current.onSelect = onSelect;

  // Category tab changes clear hover so the tree callout / highlight don't linger.
  useEffect(() => {
    stateRef.current.hoveredId = null;
    calloutRef.current?.classList.remove("is-visible");
    if (svgRef.current) svgRef.current.style.opacity = "0";
    hostRef.current?.querySelector("canvas")?.classList.remove("is-leaf-hover");
  }, [activeCategory]);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return undefined;

    const itemsById = new Map(recognitions.map((item) => [item.id, item]));

    const textures = {
      bark: createBarkTexture(),
      barkBump: createBarkBumpTexture(),
      backdrop: createBackdropTexture(),
      ground: createGroundTexture(),
      leaf: createLeafTexture(),
      grass: createGrassTexture(),
      flowers: [
        createFlowerTexture("rgba(232, 156, 176, 0.95)", "rgba(255, 220, 120, 0.95)"),
        createFlowerTexture("rgba(255, 214, 160, 0.95)", "rgba(255, 240, 180, 0.95)"),
        createFlowerTexture("rgba(186, 168, 220, 0.95)", "rgba(255, 230, 150, 0.95)"),
        createFlowerTexture("rgba(255, 255, 255, 0.92)", "rgba(255, 236, 160, 0.95)"),
      ],
    };

    const scene = new THREE.Scene();
    scene.background = textures.backdrop;
    scene.fog = new THREE.Fog(FOG_COLOR, 11, 24);

    const camera = new THREE.PerspectiveCamera(34, 1, 0.1, 50);
    let distance = DEFAULT_DISTANCE;
    applyCameraDistance(camera, host, distance);

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: "high-performance" });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.08;
    host.appendChild(renderer.domElement);

    const hemi = new THREE.HemisphereLight(0xfffaf0, 0xd9ccb4, 1.1);
    scene.add(hemi);
    const sun = new THREE.DirectionalLight(0xfff3dd, 1.0);
    sun.position.set(4.2, 7.5, 3.2);
    scene.add(sun);
    const fill = new THREE.DirectionalLight(0xf0e6ff, 0.28);
    fill.position.set(-3.5, 2.2, -2.5);
    scene.add(fill);
    const rim = new THREE.DirectionalLight(0xffe4c8, 0.24);
    rim.position.set(-1.5, 3.5, 4);
    scene.add(rim);

    // Soft cream dome so rotating keeps the studio backdrop seamless.
    const dome = new THREE.Mesh(
      new THREE.SphereGeometry(22, 32, 16),
      new THREE.MeshBasicMaterial({ map: textures.backdrop, side: THREE.BackSide, depthWrite: false, fog: false }),
    );
    scene.add(dome);

    const base = buildBase(scene, textures.ground);

    const tree = new THREE.Group();
    scene.add(tree);
    const anchors = buildTreeStructure(tree, textures.bark, textures.barkBump);
    scatterFoliage(tree, anchors, textures.leaf);
    const { leafGroup, leafMeshes } = placeLeaves(recognitions, anchors, textures.leaf);
    tree.add(leafGroup);
    const flora = scatterGroundFlora(base, textures.grass, textures.flowers);

    stateRef.current.leafMeshes = leafMeshes;
    stateRef.current.hoveredId = null;
    stateRef.current.rotationY = 0;
    stateRef.current.targetRotationY = 0;

    const raycaster = new THREE.Raycaster();
    const pointer = new THREE.Vector2();
    const activePointers = new Map();
    let frame = 0;
    let disposed = false;
    let rotatePointerId = null;
    let startX = 0;
    let startY = 0;
    let lastX = 0;
    let dragMoved = false;
    let pinchStartSpan = 0;
    let pinchStartDistance = DEFAULT_DISTANCE;
    let lastFocusKey = -1;
    const DRAG_THRESHOLD = 6;

    const setDistance = (next) => {
      distance = clamp(next, MIN_DISTANCE, MAX_DISTANCE);
      applyCameraDistance(camera, host, distance);
    };

    const resize = () => {
      const { clientWidth: width, clientHeight: height } = host;
      if (!width || !height) return;
      camera.aspect = width / height;
      applyCameraDistance(camera, host, distance);
      renderer.setSize(width, height, false);
      svgRef.current?.setAttribute("viewBox", `0 0 ${width} ${height}`);
    };

    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(host);
    resize();

    const applyLeafStates = () => {
      const { activeCategory: cat, selectedId: selected, hoveredId: hovered } = stateRef.current;
      for (const leaf of leafMeshes) {
        const matchCat = cat === "all" || leaf.userData.category === cat;
        const isSelected = leaf.userData.id === selected;
        const isHovered = leaf.userData.id === hovered;
        const mat = leaf.material;
        mat.opacity = matchCat ? (isSelected || isHovered ? 1 : 0.9) : 0.16;
        leaf.scale.setScalar(isSelected ? 1.32 : isHovered ? 1.2 : matchCat ? 1 : 0.82);
        if (isSelected || isHovered) {
          mat.emissive.set(categoryById[leaf.userData.category]?.color ?? "#ffffff");
          mat.emissiveIntensity = isSelected ? 0.26 : 0.18;
        } else {
          mat.emissive.set("#000000");
          mat.emissiveIntensity = 0;
        }
      }
    };

    /** Project the hovered leaf to screen space and lay out the callout + connector. */
    const hideCallout = () => {
      calloutRef.current?.classList.remove("is-visible");
      if (svgRef.current) svgRef.current.style.opacity = "0";
    };

    const updateCallout = () => {
      const callout = calloutRef.current;
      const svg = svgRef.current;
      const line = lineRef.current;
      const dot = dotRef.current;
      if (!callout || !svg || !line || !dot) return;

      const { hoveredId: hovered, activeCategory: cat } = stateRef.current;
      const leaf = hovered ? leafMeshes.find((entry) => entry.userData.id === hovered) : null;
      const item = hovered ? itemsById.get(hovered) : null;
      const matchCat = leaf && (cat === "all" || leaf.userData.category === cat);

      if (!leaf || !item || !matchCat) {
        hideCallout();
        return;
      }

      const width = host.clientWidth;
      const height = host.clientHeight;
      const world = leaf.getWorldPosition(new THREE.Vector3());
      const projected = world.clone().project(camera);
      if (projected.z > 1) {
        hideCallout();
        return;
      }
      const x = (projected.x * 0.5 + 0.5) * width;
      const y = (-projected.y * 0.5 + 0.5) * height;

      if (calloutTimeRef.current) calloutTimeRef.current.textContent = item.meta ?? "";
      if (calloutTitleRef.current) calloutTitleRef.current.textContent = item.title ?? "";
      callout.classList.add("is-visible");

      // Measure after text is set, then keep the bubble fully on screen.
      const calloutWidth = callout.offsetWidth || 120;
      const calloutHeight = callout.offsetHeight || 44;
      const dir = x < width / 2 ? -1 : 1;
      const endX = clamp(x + dir * 56, calloutWidth / 2 + 10, width - calloutWidth / 2 - 10);
      const endY = clamp(y - 46, calloutHeight + 20, height - 24);

      const color = categoryById[leaf.userData.category]?.color ?? "#8a8073";
      dot.setAttribute("cx", `${x}`);
      dot.setAttribute("cy", `${y}`);
      dot.setAttribute("fill", color);
      line.setAttribute("x1", `${x}`);
      line.setAttribute("y1", `${y}`);
      line.setAttribute("x2", `${endX}`);
      line.setAttribute("y2", `${endY}`);
      line.setAttribute("stroke", color);
      svg.style.opacity = "1";

      callout.style.borderColor = color;
      callout.style.left = `${endX}px`;
      callout.style.top = `${endY}px`;
    };

    const focusLeaf = (id) => {
      const leaf = leafMeshes.find((entry) => entry.userData.id === id);
      if (!leaf) return;
      stateRef.current.targetRotationY = rotationToFaceLeaf(leaf.userData.basePosition, camera);
    };

    const pickLeaf = (clientX, clientY) => {
      const rect = renderer.domElement.getBoundingClientRect();
      pointer.x = ((clientX - rect.left) / rect.width) * 2 - 1;
      pointer.y = -((clientY - rect.top) / rect.height) * 2 + 1;
      raycaster.setFromCamera(pointer, camera);
      const hits = raycaster.intersectObjects(leafMeshes, false);
      return hits.length > 0 ? hits[0].object : null;
    };

    const pointerSpan = () => {
      if (activePointers.size < 2) return 0;
      const [a, b] = activePointers.values();
      return Math.hypot(a.x - b.x, a.y - b.y);
    };

    const onPointerDown = (event) => {
      activePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
      renderer.domElement.setPointerCapture?.(event.pointerId);

      if (activePointers.size === 2) {
        rotatePointerId = null;
        stateRef.current.dragging = false;
        dragMoved = false;
        pinchStartSpan = pointerSpan();
        pinchStartDistance = distance;
        renderer.domElement.classList.remove("is-dragging");
        return;
      }

      rotatePointerId = event.pointerId;
      startX = event.clientX;
      startY = event.clientY;
      lastX = event.clientX;
      dragMoved = false;
      stateRef.current.dragging = true;
      renderer.domElement.classList.add("is-dragging");
    };

    const onPointerMove = (event) => {
      // Pure hover (no captured pointers): highlight the leaf and its callout.
      if (activePointers.size === 0) {
        if (event.pointerType === "mouse") {
          const hit = pickLeaf(event.clientX, event.clientY);
          stateRef.current.hoveredId = hit ? hit.userData.id : null;
          renderer.domElement.classList.toggle("is-leaf-hover", Boolean(hit));
        }
        return;
      }

      if (!activePointers.has(event.pointerId)) return;
      activePointers.set(event.pointerId, { x: event.clientX, y: event.clientY });

      if (activePointers.size >= 2 && pinchStartSpan > 0) {
        const span = pointerSpan();
        if (span > 0) {
          setDistance(pinchStartDistance * (pinchStartSpan / span));
        }
        return;
      }

      if (rotatePointerId !== event.pointerId || !stateRef.current.dragging) return;
      const dx = event.clientX - lastX;
      const totalDx = event.clientX - startX;
      const totalDy = event.clientY - startY;
      if (!dragMoved && Math.hypot(totalDx, totalDy) > DRAG_THRESHOLD) {
        if (Math.abs(totalDx) >= Math.abs(totalDy)) {
          dragMoved = true;
        } else {
          stateRef.current.dragging = false;
          rotatePointerId = null;
          renderer.domElement.classList.remove("is-dragging");
          return;
        }
      }
      if (!dragMoved) return;
      lastX = event.clientX;
      const next = stateRef.current.rotationY + dx * 0.008;
      stateRef.current.rotationY = next;
      stateRef.current.targetRotationY = next;
    };

    const onPointerLeave = () => {
      if (activePointers.size === 0) {
        stateRef.current.hoveredId = null;
        renderer.domElement.classList.remove("is-leaf-hover");
      }
    };

    const onPointerUp = (event) => {
      const wasRotate = rotatePointerId === event.pointerId;
      const wasDragging = stateRef.current.dragging;
      const didDrag = dragMoved;
      activePointers.delete(event.pointerId);
      renderer.domElement.releasePointerCapture?.(event.pointerId);

      if (activePointers.size < 2) {
        pinchStartSpan = 0;
      }

      if (activePointers.size === 1) {
        const [remainingId, point] = [...activePointers.entries()][0];
        rotatePointerId = remainingId;
        startX = point.x;
        startY = point.y;
        lastX = point.x;
        dragMoved = false;
        stateRef.current.dragging = true;
        renderer.domElement.classList.add("is-dragging");
        return;
      }

      if (wasRotate) {
        rotatePointerId = null;
        stateRef.current.dragging = false;
        renderer.domElement.classList.remove("is-dragging");
      }

      if (!wasRotate || !wasDragging || didDrag || activePointers.size > 0) return;

      const hit = pickLeaf(event.clientX, event.clientY);
      if (hit) {
        const id = hit.userData.id;
        focusLeaf(id);
        stateRef.current.onSelect?.(id);
      }
    };

    const onPointerCancel = (event) => {
      activePointers.delete(event.pointerId);
      if (rotatePointerId === event.pointerId) {
        rotatePointerId = null;
        stateRef.current.dragging = false;
        renderer.domElement.classList.remove("is-dragging");
      }
      if (activePointers.size < 2) pinchStartSpan = 0;
    };

    const onWheel = (event) => {
      event.preventDefault();
      setDistance(distance + event.deltaY * 0.012);
    };

    renderer.domElement.addEventListener("pointerdown", onPointerDown);
    renderer.domElement.addEventListener("pointermove", onPointerMove);
    renderer.domElement.addEventListener("pointerup", onPointerUp);
    renderer.domElement.addEventListener("pointercancel", onPointerCancel);
    renderer.domElement.addEventListener("pointerleave", onPointerLeave);
    renderer.domElement.addEventListener("wheel", onWheel, { passive: false });

    const clock = new THREE.Clock();
    const animate = () => {
      if (disposed) return;
      frame = requestAnimationFrame(animate);
      const t = clock.getElapsedTime();

      const { selectedId: selected, focusKey: key } = stateRef.current;
      if (selected && key !== lastFocusKey && !stateRef.current.dragging) {
        lastFocusKey = key;
        focusLeaf(selected);
      }

      if (!stateRef.current.dragging) {
        stateRef.current.rotationY = lerpAngle(
          stateRef.current.rotationY,
          stateRef.current.targetRotationY,
          0.1,
        );
      }
      tree.rotation.y = stateRef.current.rotationY;
      flora.children.forEach((child) => {
        if (child.userData?.kind === "flower") {
          child.rotation.z = Math.sin(t * 1.2 + child.userData.sway) * 0.08;
        }
      });

      for (const leaf of leafMeshes) {
        const { phase, amp, basePosition, baseRotationZ } = leaf.userData;
        leaf.position.y = basePosition.y + Math.sin(t * 1.35 + phase) * amp;
        leaf.rotation.z = baseRotationZ + Math.sin(t * 1.05 + phase) * 0.07;
      }
      applyLeafStates();
      renderer.render(scene, camera);
      updateCallout();
    };
    animate();

    return () => {
      disposed = true;
      cancelAnimationFrame(frame);
      resizeObserver.disconnect();
      renderer.domElement.removeEventListener("pointerdown", onPointerDown);
      renderer.domElement.removeEventListener("pointermove", onPointerMove);
      renderer.domElement.removeEventListener("pointerup", onPointerUp);
      renderer.domElement.removeEventListener("pointercancel", onPointerCancel);
      renderer.domElement.removeEventListener("pointerleave", onPointerLeave);
      renderer.domElement.removeEventListener("wheel", onWheel);
      Object.values(textures).forEach((value) => {
        if (Array.isArray(value)) value.forEach((tex) => tex.dispose());
        else value.dispose();
      });
      scene.traverse((obj) => {
        if (obj.geometry) obj.geometry.dispose();
        if (obj.material) {
          if (Array.isArray(obj.material)) obj.material.forEach((m) => m.dispose());
          else obj.material.dispose();
        }
      });
      renderer.dispose();
      if (renderer.domElement.parentNode === host) host.removeChild(renderer.domElement);
    };
  }, [recognitions]);

  return (
    <div className="life-tree-canvas" ref={hostRef} role="img" aria-label="生命之树可视化">
      <svg className="life-tree-callout-svg" ref={svgRef} aria-hidden="true">
        <line ref={lineRef} strokeWidth="1.5" strokeDasharray="none" />
        <circle ref={dotRef} r="3.5" />
      </svg>
      <div className="life-tree-callout" ref={calloutRef}>
        <small ref={calloutTimeRef} />
        <strong ref={calloutTitleRef} />
      </div>
    </div>
  );
}
