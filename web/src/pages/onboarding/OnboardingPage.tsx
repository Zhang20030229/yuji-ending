// @ts-nocheck -- Migrated visual prototype; runtime behavior is intentionally preserved verbatim.
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { orbitRecords, scenes } from "./scenes";
import "./onboarding.css";

const STORAGE_KEY = "yuji.onboarding.completed";
const PENDING_KEY = "yuji.onboarding.pending";
const AUTOPLAY_DURATION = 26000;
const AUTOPLAY_DELAY = 900;

const clamp = (value, min = 0, max = 1) => Math.min(max, Math.max(min, value));
const lerp = (from, to, amount) => from + (to - from) * amount;
const smoothstep = (from, to, value) => {
  const amount = clamp((value - from) / (to - from));
  return amount * amount * (3 - 2 * amount);
};
const windowOpacity = (progress, enterFrom, enterTo, exitFrom, exitTo) =>
  smoothstep(enterFrom, enterTo, progress) * (1 - smoothstep(exitFrom, exitTo, progress));
const mixRgb = (from, to, amount) => {
  const values = from.map((value, index) => Math.round(lerp(value, to[index], amount)));
  return `rgb(${values.join(" ")})`;
};

function navigate(path, setPath) {
  setPath(path);
  window.scrollTo(0, 0);
}

function OrbitIcon({ type }) {
  if (type === "mood") {
    return <path d="M12 20.2 4.8 13a4.8 4.8 0 0 1 6.8-6.8l.4.5.4-.5a4.8 4.8 0 0 1 6.8 6.8L12 20.2Z" />;
  }
  if (type === "insight") {
    return <><path d="m9.5 4 .9 2.8 2.7.9-2.7.9-.9 2.8-.9-2.8-2.7-.9 2.7-.9.9-2.8Z" /><path d="m16.8 11.5 1.1 3.2 3.1 1.1-3.1 1-1.1 3.2-1-3.2-3.2-1 3.2-1.1 1-3.2ZM18.8 4v3M17.3 5.5h3" /></>;
  }
  if (type === "people") {
    return <><circle cx="10" cy="7.2" r="3" /><path d="M4.8 18.7v-1.2a5.2 5.2 0 0 1 10.4 0v1.2M16 5.1a2.8 2.8 0 0 1 0 5.4M17.3 13.2a4.6 4.6 0 0 1 2.4 4.1v1" /></>;
  }
  if (type === "knowledge") {
    return <><path d="M9.4 4.2a3 3 0 0 0-4 4.4 3.2 3.2 0 0 0 .2 5.8A3.1 3.1 0 0 0 9.4 19V4.2ZM14.6 4.2a3 3 0 0 1 4 4.4 3.2 3.2 0 0 1-.2 5.8 3.1 3.1 0 0 1-3.8 4.6V4.2Z" /><path d="M9.4 8.2H7.7M9.4 14H7.3M14.6 8.2h1.7M14.6 14h2.1" /></>;
  }
  if (type === "pending") {
    return <><rect x="4" y="4.5" width="16" height="15" rx="2.5" /><path d="M4 14h4l2 2h4l2-2h4" /></>;
  }
  if (type === "moment") {
    return <><rect x="3.5" y="4" width="17" height="16" rx="2.8" /><circle cx="15.8" cy="8.4" r="1.5" /><path d="m5.8 17 4.1-4.3 3.1 2.8 2.1-2 3.1 3.5" /></>;
  }
  return <><path d="M6 3.5h8l4 4V20H6V3.5Z" /><path d="M14 3.5V8h4M9 16v-3M12 16v-5M15 16v-2" /></>;
}

function AmbientScene() {
  return (
    <div className="ambient-scene" aria-hidden="true">
      <div className="ambient-scene__image" />
      <div className="ambient-scene__image-bright" />
      <div className="ambient-scene__warmth" />
      <div className="ambient-scene__shade" />
      <div className="ambient-scene__vignette" />
      <div className="ambient-scene__grain" />
    </div>
  );
}

function ContinuousCopy() {
  return (
    <div className="copy-stack">
      {scenes.slice(0, 4).map((scene, index) => (
        <div className={`scene-copy scene-copy--${index}`} key={`${index}-${scene.title.join("")}`} aria-hidden="true">
          {scene.label ? (
            <div className="scene-copy__label">
              <i />
              <strong>{scene.label}</strong>
              <i />
            </div>
          ) : null}
          <h1>
            {scene.title.map((line) => <span key={line}>{line}</span>)}
          </h1>
          <p>{scene.description}</p>
        </div>
      ))}
    </div>
  );
}

function OrbitingIcons({ orbitRef }) {
  return (
    <div className="orbit-system" ref={orbitRef} aria-hidden="true">
      {orbitRecords.map((record, index) => (
        <span
          className="orbit-icon"
          key={record.title}
          data-index={index}
        >
          <svg viewBox="0 0 24 24"><OrbitIcon type={record.type} /></svg>
        </span>
      ))}
    </div>
  );
}

function ChessPiece() {
  return (
    <div className="chess-piece" aria-hidden="true">
      <div className="chess-piece__halo" />
      <img className="chess-piece__pawn" src="/assets/pawn.webp" alt="" draggable="false" />
      <img className="chess-piece__queen" src="/assets/queen.webp" alt="" draggable="false" />
      <div className="chess-piece__shadow" />
    </div>
  );
}

function FinalInvitation({ containerRef, onComplete, isCompleting }) {
  return (
    <div className="final-invitation" ref={containerRef} aria-hidden="true">
      <div className="app-lockup">
        <img src="/assets/yuji-logo.svg" alt="" />
        <span>
          <strong>遇己</strong>
          <small>ECHORA</small>
        </span>
      </div>
      <h1>升变，从此刻开始</h1>
      <p>进入遇己，看见自己的棋盘。</p>
      <button
        className="journey-option"
        onClick={onComplete}
        disabled={isCompleting}
        aria-busy={isCompleting}
      >
        <span className="journey-option__text">开始成长</span>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h13M14 7l5 5-5 5" /></svg>
      </button>
    </div>
  );
}

function ScrollProgress() {
  return (
    <div className="scroll-progress" aria-hidden="true">
      <span className="scroll-progress__hint"><i /> 向上滑动</span>
      <span className="scroll-progress__track"><i /></span>
    </div>
  );
}

function OnboardingPage({ setPath }) {
  const scrollerRef = useRef(null);
  const sceneRef = useRef(null);
  const orbitRef = useRef(null);
  const finalRef = useRef(null);
  const skipRef = useRef(null);
  const frameRef = useRef(0);
  const completionTimerRef = useRef(0);
  const autoplayFrameRef = useRef(0);
  const autoplayDelayRef = useRef(0);
  const autoplayLastTimeRef = useRef(0);
  const autoplayActiveRef = useRef(false);
  const cueRef = useRef(-1);
  const reducedMotionRef = useRef(false);
  const [accessibleCue, setAccessibleCue] = useState(0);
  const [isCompleting, setIsCompleting] = useState(false);

  const complete = useCallback(() => {
    localStorage.setItem(STORAGE_KEY, "true");
    localStorage.removeItem(PENDING_KEY);
    navigate("/app/home", setPath);
  }, [setPath]);

  const beginJourney = useCallback(() => {
    if (isCompleting) return;
    setIsCompleting(true);
    completionTimerRef.current = window.setTimeout(
      complete,
      reducedMotionRef.current ? 220 : 720,
    );
  }, [complete, isCompleting]);

  const stopAutoplay = useCallback(() => {
    autoplayActiveRef.current = false;
    autoplayLastTimeRef.current = 0;
    if (autoplayFrameRef.current) {
      cancelAnimationFrame(autoplayFrameRef.current);
      autoplayFrameRef.current = 0;
    }
    if (autoplayDelayRef.current) {
      window.clearTimeout(autoplayDelayRef.current);
      autoplayDelayRef.current = 0;
    }
  }, []);

  const autoplayTick = useCallback((time) => {
    const scroller = scrollerRef.current;
    if (!autoplayActiveRef.current || !scroller || document.hidden) {
      autoplayFrameRef.current = 0;
      autoplayLastTimeRef.current = 0;
      return;
    }

    if (!autoplayLastTimeRef.current) autoplayLastTimeRef.current = time;
    const elapsed = Math.min(time - autoplayLastTimeRef.current, 48);
    autoplayLastTimeRef.current = time;
    const maxScroll = Math.max(scroller.scrollHeight - scroller.clientHeight, 1);
    const nextScrollTop = Math.min(
      maxScroll,
      scroller.scrollTop + (maxScroll / AUTOPLAY_DURATION) * elapsed,
    );
    scroller.scrollTop = nextScrollTop;

    if (nextScrollTop < maxScroll) {
      autoplayFrameRef.current = requestAnimationFrame(autoplayTick);
    } else {
      autoplayActiveRef.current = false;
      autoplayFrameRef.current = 0;
      autoplayLastTimeRef.current = 0;
    }
  }, []);

  const renderProgress = useCallback(() => {
    frameRef.current = 0;
    const scroller = scrollerRef.current;
    const scene = sceneRef.current;
    const orbit = orbitRef.current;
    const final = finalRef.current;
    const skip = skipRef.current;
    if (!scroller || !scene || !orbit || !final || !skip) return;

    const maxScroll = scroller.scrollHeight - scroller.clientHeight;
    const progress = maxScroll > 0 ? clamp(scroller.scrollTop / maxScroll) : 0;

    const travel = smoothstep(0.02, 0.61, progress);
    const pawnY = lerp(73, 56, travel);
    const orbitEntrance = smoothstep(0.14, 0.24, progress);
    const orbitExit = 1 - smoothstep(0.71, 0.77, progress);
    const orbitOpacity = orbitEntrance * orbitExit;
    const orbitTime = clamp((progress - 0.16) / 0.58);
    const turns = 0.12 * orbitTime + 2.9 * orbitTime * orbitTime * orbitTime;
    const rotation = reducedMotionRef.current
      ? lerp(0, Math.PI / 4, orbitTime)
      : turns * Math.PI * 2;
    const gather = smoothstep(0.56, 0.74, progress);
    const gatherCurve = gather * gather * gather;
    const orbitCenterY = pawnY - 10.5;
    const flash = smoothstep(0.70, 0.76, progress) * (1 - smoothstep(0.79, 0.845, progress));
    const queen = smoothstep(0.765, 0.84, progress);
    const bright = smoothstep(0.73, 0.84, progress) * (1 - smoothstep(0.88, 0.985, progress));
    const finalAmount = smoothstep(0.875, 0.985, progress);
    const shadeOpacity = clamp(lerp(0.67, 0.04, bright) + finalAmount * 0.72, 0.04, 0.78);
    const pieceWidth = lerp(88, 106, queen) + finalAmount * 18;
    const pieceHeight = lerp(180, 232, queen) + finalAmount * 32;
    const pieceY = lerp(pawnY, 48, finalAmount);

    scene.style.setProperty("--progress", progress.toFixed(5));
    scene.style.setProperty("--pawn-y", `${pieceY.toFixed(3)}%`);
    scene.style.setProperty("--piece-width", `${pieceWidth.toFixed(2)}px`);
    scene.style.setProperty("--piece-height", `${pieceHeight.toFixed(2)}px`);
    scene.style.setProperty("--pawn-opacity", (1 - queen).toFixed(4));
    scene.style.setProperty("--queen-opacity", queen.toFixed(4));
    scene.style.setProperty("--piece-opacity", (1 - finalAmount * 0.82).toFixed(4));
    scene.style.setProperty("--halo-opacity", clamp(gather * 0.9 + queen * 0.58 - finalAmount * 1.1).toFixed(4));
    scene.style.setProperty("--orbit-opacity", orbitOpacity.toFixed(4));
    scene.style.setProperty("--orbit-y", `${orbitCenterY.toFixed(3)}%`);
    scene.style.setProperty("--flash", flash.toFixed(4));
    scene.style.setProperty("--bright", bright.toFixed(4));
    scene.style.setProperty("--shade", shadeOpacity.toFixed(4));
    scene.style.setProperty("--final", finalAmount.toFixed(4));
    scene.style.setProperty("--final-y", `${lerp(16, 0, finalAmount).toFixed(2)}px`);
    scene.style.setProperty("--skip-opacity", (0.38 * (1 - smoothstep(0.82, 0.93, progress))).toFixed(4));
    scene.style.setProperty("--copy-color", mixRgb([247, 241, 231], [58, 38, 25], bright));
    scene.style.setProperty("--copy-muted", mixRgb([210, 201, 188], [92, 60, 39], bright));

    const copyValues = [
      windowOpacity(progress, -0.02, 0.025, 0.13, 0.19),
      windowOpacity(progress, 0.19, 0.235, 0.41, 0.465),
      windowOpacity(progress, 0.47, 0.515, 0.675, 0.725),
      windowOpacity(progress, 0.735, 0.79, 0.875, 0.93),
    ];
    copyValues.forEach((value, index) => {
      scene.style.setProperty(`--copy-${index}`, value.toFixed(4));
      scene.style.setProperty(`--copy-y-${index}`, `${lerp(8, 0, value).toFixed(2)}px`);
    });

    const icons = orbit.querySelectorAll(".orbit-icon");
    icons.forEach((icon, index) => {
      const angle = rotation + (index / icons.length) * Math.PI * 2 - Math.PI / 2;
      const radius = lerp(94, 0, gatherCurve);
      const verticalRadius = radius * 0.42;
      const sine = Math.sin(angle);
      const x = Math.cos(angle) * radius;
      const y = sine * verticalRadius;
      const depthProgress = (sine + 1) / 2;
      const depth = lerp(0.72, 1.16, depthProgress);
      const iconOpacity = orbitOpacity * lerp(0.5, 1, depthProgress);
      const blur = lerp(0.7, 0, depthProgress);
      icon.style.transform = `translate3d(calc(-50% + ${x.toFixed(2)}px), calc(-50% + ${y.toFixed(2)}px), 0) scale(${depth.toFixed(3)})`;
      icon.style.opacity = iconOpacity.toFixed(4);
      icon.style.zIndex = depthProgress > 0.52 ? "9" : "4";
      icon.style.filter = `blur(${blur.toFixed(2)}px)`;
      icon.style.setProperty("--icon-front-light", depthProgress.toFixed(3));
      icon.style.setProperty("--glow-strength", `${lerp(9, 22, gather).toFixed(1)}px`);
    });

    const finalIsInteractive = progress >= 0.955;
    final.inert = !finalIsInteractive;
    final.setAttribute("aria-hidden", finalIsInteractive ? "false" : "true");
    const skipIsInteractive = progress < 0.9;
    skip.inert = !skipIsInteractive;
    skip.setAttribute("aria-hidden", skipIsInteractive ? "false" : "true");
    skip.style.pointerEvents = skipIsInteractive ? "auto" : "none";

    const nextCue = progress < 0.18 ? 0 : progress < 0.47 ? 1 : progress < 0.75 ? 2 : progress < 0.9 ? 3 : 4;
    if (nextCue !== cueRef.current) {
      cueRef.current = nextCue;
      setAccessibleCue(nextCue);
    }

  }, []);

  const scheduleRender = useCallback(() => {
    if (frameRef.current) return;
    frameRef.current = requestAnimationFrame(renderProgress);
  }, [renderProgress]);

  useLayoutEffect(() => {
    const motionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const updateMotionPreference = () => {
      reducedMotionRef.current = motionQuery.matches;
      if (motionQuery.matches) stopAutoplay();
      scheduleRender();
    };
    reducedMotionRef.current = motionQuery.matches;
    renderProgress();
    window.addEventListener("resize", scheduleRender);
    motionQuery.addEventListener("change", updateMotionPreference);
    return () => {
      window.removeEventListener("resize", scheduleRender);
      motionQuery.removeEventListener("change", updateMotionPreference);
      if (frameRef.current) cancelAnimationFrame(frameRef.current);
      if (completionTimerRef.current) window.clearTimeout(completionTimerRef.current);
    };
  }, [renderProgress, scheduleRender, stopAutoplay]);

  useEffect(() => {
    if (reducedMotionRef.current) return undefined;

    autoplayActiveRef.current = true;
    const resumeAutoplay = () => {
      if (
        autoplayActiveRef.current
        && !document.hidden
        && !autoplayFrameRef.current
      ) {
        autoplayLastTimeRef.current = 0;
        autoplayFrameRef.current = requestAnimationFrame(autoplayTick);
      }
    };
    const handleVisibilityChange = () => {
      if (document.hidden) {
        if (autoplayFrameRef.current) cancelAnimationFrame(autoplayFrameRef.current);
        autoplayFrameRef.current = 0;
        autoplayLastTimeRef.current = 0;
      } else {
        resumeAutoplay();
      }
    };

    autoplayDelayRef.current = window.setTimeout(() => {
      autoplayDelayRef.current = 0;
      resumeAutoplay();
    }, AUTOPLAY_DELAY);
    document.addEventListener("visibilitychange", handleVisibilityChange);

    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      stopAutoplay();
    };
  }, [autoplayTick, stopAutoplay]);

  const handleKeyDown = (event) => {
    stopAutoplay();
    const scroller = scrollerRef.current;
    if (!scroller) return;
    let distance = 0;
    if (event.key === "ArrowDown" || event.key === "ArrowRight") distance = scroller.clientHeight * 0.52;
    if (event.key === "ArrowUp" || event.key === "ArrowLeft") distance = scroller.clientHeight * -0.52;
    if (event.key === "PageDown") distance = scroller.clientHeight * 0.88;
    if (event.key === "PageUp") distance = scroller.clientHeight * -0.88;
    if (distance) {
      event.preventDefault();
      scroller.scrollBy({ top: distance, behavior: "smooth" });
    }
    if (event.key === "Escape") event.preventDefault();
  };

  const cueText = isCompleting
    ? "正在开启新的旅程"
    : accessibleCue === 4
    ? "升变，从此刻开始。进入遇己，看见自己的棋盘。"
    : [
        scenes[accessibleCue].label,
        scenes[accessibleCue].title.join(""),
        scenes[accessibleCue].description,
      ].filter(Boolean).join("");

  return (
    <main className={`onboarding-v2${isCompleting ? " is-completing" : ""}`}>
      <div
        className="portrait-scroller"
        ref={scrollerRef}
        onScroll={scheduleRender}
        onPointerDown={stopAutoplay}
        onWheel={stopAutoplay}
        onKeyDown={handleKeyDown}
        tabIndex={0}
        aria-label="首次引导，可上下滚动探索"
      >
        <section className="scene-viewport" ref={sceneRef} aria-label="成长与升变">
          <AmbientScene />
          <button className="skip-button-v2" ref={skipRef} onClick={complete}>跳过</button>
          <ContinuousCopy />
          <div className="journey-stage">
            <OrbitingIcons orbitRef={orbitRef} />
            <ChessPiece />
          </div>
          <div className="light-bloom" aria-hidden="true" />
          <FinalInvitation
            containerRef={finalRef}
            onComplete={beginJourney}
            isCompleting={isCompleting}
          />
          <div className="completion-flash" aria-hidden="true" />
          <div className="sr-only" aria-live="polite">{cueText}</div>
          <ScrollProgress />
        </section>
        <div className="scroll-spacer" aria-hidden="true" />
      </div>
    </main>
  );
}

/** 新注册用户完成资料后只展示一次的产品引导。 */
export default function ProductOnboardingPage() {
  const navigateTo = useNavigate();
  return <OnboardingPage setPath={(path) => navigateTo(path, { replace: true })} />;
}
