/* =====================================================================
   FH6 Tuning Calculator — UI controller
   Reads inputs (live), runs the engine, renders cards / compare table.
   Engine works in imperial; we convert metric in/out here.
   ===================================================================== */
(function () {
  "use strict";
  const { GOALS, GOAL_META, compute, validate, overallTireDiameter } = window.TUNING;
  const SETUPS = window.SETUPS;

  /* ---------- unit handling ---------- */
  // factor to convert a METRIC value to IMPERIAL (multiply); divide to go back.
  // aero force: 1 kgf = 2.2046226 lbf (same numeric factor as mass).
  const M2I = { weight: 2.2046226, torque: 0.7375621, ride: 0.3937008, spring: 55.99741, aero: 2.2046226, speed: 0.6213712 };
  const UNIT_LABEL = {
    imperial: { weight: "(lb)", torque: "(lb-ft)", ride: "(in)", spring: "(lb/in)", power: "(hp)", aero: "(lbf)", speed: "(mph)" },
    metric:   { weight: "(kg)", torque: "(Nm)",    ride: "(cm)", spring: "(kgf/mm)", power: "(hp)", aero: "(kgf)", speed: "(km/h)" },
  };
  const FIELD_DIM = {
    weight: "weight", torque: "torque",
    rideHeightMinF: "ride", rideHeightMaxF: "ride", rideHeightMinR: "ride", rideHeightMaxR: "ride",
    springRateMinF: "spring", springRateMaxF: "spring", springRateMinR: "spring", springRateMaxR: "spring",
    aeroFrontMin: "aero", aeroFrontMax: "aero", aeroRearMin: "aero", aeroRearMax: "aero",
    targetTopSpeed: "speed",
    // note: tire width (mm), aspect (%), rim (in) are unit-independent in Forza —
    // they stay OUT of FIELD_DIM so the metric toggle never rewrites them.
  };
  let units = "imperial";

  const $ = (id) => document.getElementById(id);
  const num = (id) => parseFloat($(id).value) || 0;

  function toImp(dim, v) { return units === "metric" ? v * M2I[dim] : v; }
  function fromImp(dim, v) { return units === "metric" ? v / M2I[dim] : v; }

  // pretty number: trims trailing zeros, keeps up to `dp` decimals
  function nf(v, dp = 1) {
    if (v === null || v === undefined || isNaN(v)) return "—";
    const s = Number(v).toFixed(dp);
    return s.replace(/\.0+$/, "").replace(/(\.\d*?)0+$/, "$1");
  }
  const springDisp = (vImp) => nf(fromImp("spring", vImp), units === "metric" ? 2 : 0);
  const rideDisp = (vImp) => nf(fromImp("ride", vImp), 1);
  const aeroDisp = (vImp) => nf(fromImp("aero", vImp), 0) + (units === "metric" ? " kgf" : " lbf");
  const speedDisp = (mphImp) => nf(fromImp("speed", mphImp), 0) + (units === "metric" ? " km/h" : " mph");
  // optional single numeric field → imperial number (via dim) or null when blank/invalid
  function optField(id, dim) {
    const s = $(id).value.trim();
    if (s === "") return null;
    const v = parseFloat(s);
    if (!isFinite(v)) return null;
    return dim ? toImp(dim, v) : v;
  }
  // format an aero end: lbf (with % in parens) if a range was given, else % of slider, else the absent label
  const aeroVal = (pct, lbfImp, absent) => pct == null ? absent : (lbfImp != null ? `${aeroDisp(lbfImp)} (${pct}%)` : `${pct}% of range`);

  // optional lbf range from a min/max field pair → [impMin, impMax] or [null, null] if blank/invalid
  function optAeroRange(idMin, idMax) {
    const a = $(idMin).value.trim(), b = $(idMax).value.trim();
    if (a === "" || b === "") return [null, null];
    const lo = parseFloat(a), hi = parseFloat(b);
    if (!isFinite(lo) || !isFinite(hi)) return [null, null];
    return [toImp("aero", lo), toImp("aero", hi)];
  }

  // a part-range field (spring/ride) → imperial number, falling back to the
  // placeholder default (imperial) when the field is left blank/invalid. Lets the
  // ranges stay empty-by-default while still producing a sensible tune.
  function rangeField(id, dim, defImp) {
    const s = $(id).value.trim();
    if (s === "") return defImp;
    const v = parseFloat(s);
    return isFinite(v) ? toImp(dim, v) : defImp;
  }
  // an integer field with a default when blank (e.g. gear count)
  function intField(id, def) {
    const s = $(id).value.trim();
    if (s === "") return def;
    const v = parseFloat(s);
    return isFinite(v) ? Math.round(v) : def;
  }

  /* ---------- read all inputs into engine-ready (imperial) object ---------- */
  function readInputs() {
    return {
      drivetrain: $("drivetrain").value,
      engineLocation: $("engineLocation").value,
      powertrain: $("powertrain").value,
      piClass: $("piClass").value,
      power: num("power"),
      torque: toImp("torque", num("torque")),
      weight: toImp("weight", num("weight")),
      frontWeightPct: num("frontWeight"),
      gears: intField("gears", 6),
      tireCompound: $("tireCompound").value,
      suspensionType: $("suspensionType").value,
      // aero kit: None | Front (splitter only) | Rear (wing only) | Full
      hasFrontAero: (function () { const k = $("aeroKit").value; return k === "Front" || k === "Full"; })(),
      hasRearAero: (function () { const k = $("aeroKit").value; return k === "Rear" || k === "Full"; })(),
      aeroInstalled: $("aeroKit").value !== "None", // any wing -> secondary downforce effects apply
      rideHeightMinF: rangeField("rideHeightMinF", "ride", 4.5),
      rideHeightMaxF: rangeField("rideHeightMaxF", "ride", 7.0),
      rideHeightMinR: rangeField("rideHeightMinR", "ride", 4.5),
      rideHeightMaxR: rangeField("rideHeightMaxR", "ride", 7.0),
      springRateMinF: rangeField("springRateMinF", "spring", 150),
      springRateMaxF: rangeField("springRateMaxF", "spring", 900),
      springRateMinR: rangeField("springRateMinR", "spring", 150),
      springRateMaxR: rangeField("springRateMaxR", "spring", 900),
      // optional downforce ranges (imperial lbf, or null = show % of slider)
      aeroFront: optAeroRange("aeroFrontMin", "aeroFrontMax"),
      aeroRear: optAeroRange("aeroRearMin", "aeroRearMax"),
      // optional gearing physics (null = HP heuristic). target speed in mph.
      // drive-tire overall Ø (in) is computed from the FH6 spec width/aspect/rim;
      // null when any part is blank, which falls back to the HP heuristic.
      redlineRpm: optField("redlineRpm"),
      tireDiameter: overallTireDiameter(optField("tireWidth"), optField("tireAspect"), optField("tireRim")),
      targetTopSpeed: optField("targetTopSpeed", "speed"),
      // handling bias: −5 (understeer) … 0 (neutral) … +5 (oversteer); 0 = pure baseline
      handlingBias: num("handlingBias"),
      // overall stiffness: −5 (soft) … 0 (balanced) … +5 (hard); 0 = pure baseline
      overallStiffness: num("overallStiffness"),
    };
  }

  /* ---------- drive-tire computed-diameter readout ---------- */
  // Mirrors the engine's overallTireDiameter so the user sees the rolling Ø
  // their width/aspect/rim spec resolves to (e.g. 315/30R17 → "= 24.4 in").
  function updateTireDiaOut() {
    const dia = overallTireDiameter(optField("tireWidth"), optField("tireAspect"), optField("tireRim"));
    $("tireDiaOut").textContent = dia != null ? `= ${nf(dia, 1)} in` : "= — in";
  }

  /* ---------- handling-bias slider label ---------- */
  function updateBiasLabel() {
    const b = num("handlingBias");
    const el = $("biasValue");
    el.classList.remove("under", "over");
    if (b === 0) { el.textContent = "Neutral (0)"; return; }
    const dir = b > 0 ? "Oversteer" : "Understeer";
    el.classList.add(b > 0 ? "over" : "under");
    el.textContent = `${dir} (${b > 0 ? "+" : ""}${nf(b, 1)})`;
  }

  /* ---------- overall-stiffness slider label ---------- */
  function updateStiffLabel() {
    const s = num("overallStiffness");
    const el = $("stiffValue");
    el.classList.remove("soft", "hard");
    if (s === 0) { el.textContent = "Balanced (0)"; return; }
    const dir = s > 0 ? "Hard" : "Soft";
    el.classList.add(s > 0 ? "hard" : "soft");
    el.textContent = `${dir} (${s > 0 ? "+" : ""}${nf(s, 1)})`;
  }

  /* ---------- card definitions (single-goal view) ---------- */
  function gearRows(t) {
    const g = t.gearing;
    const rows = [{ k: "Final drive", v: nf(g.final, 2) }];
    if (g.singleSpeed) {
      // EVs are single-speed but the lone gear IS an adjustable in-game slider
      // ("1st", e.g. Hummer EV defaults to 3.08) — the final drive above is only
      // correct paired with this value, so it must be shown, not hidden.
      rows.push({
        k: "1st (only gear)",
        v: nf(g.ratios[0], 2) + (g.speeds ? `  ·  ${speedDisp(g.speeds[0])}` : ""),
        sub: true,
      });
    } else {
      g.ratios.forEach((r, idx) => rows.push({
        k: `Gear ${idx + 1}`,
        v: nf(r, 2) + (g.speeds ? `  ·  ${speedDisp(g.speeds[idx])}` : ""),
        sub: true,
      }));
    }
    if (g.topSpeed != null) rows.push({ k: "Top speed @ redline", v: speedDisp(g.topSpeed) });
    return rows;
  }
  function diffRows(t) {
    const dd = t.differential;
    if (dd.driveline === "AWD") {
      return [
        { k: "Front accel", v: dd.frontAccel + "%" }, { k: "Front decel", v: dd.frontDecel + "%" },
        { k: "Rear accel", v: dd.accel + "%" }, { k: "Rear decel", v: dd.decel + "%" },
        { k: "Center (→rear)", v: dd.centerRear + "%" },
      ];
    }
    return [{ k: dd.driveline + " accel", v: dd.accel + "%" }, { k: dd.driveline + " decel", v: dd.decel + "%" }];
  }

  const CARDS = [
    { id: "tires", title: "Tires", icon: "🛞", rows: (t) => [
        { k: "Front pressure", v: nf(t.tires.front, 1) + " psi" }, { k: "Rear pressure", v: nf(t.tires.rear, 1) + " psi" }] },
    { id: "gearing", title: "Gearing", icon: "⚙️", rows: gearRows },
    { id: "alignment", title: "Alignment", icon: "📐", rows: (t) => [
        { k: "Camber front", v: nf(t.alignment.camberF, 1) + "°" }, { k: "Camber rear", v: nf(t.alignment.camberR, 1) + "°" },
        { k: "Toe front", v: nf(t.alignment.toeF, 1) + "°" }, { k: "Toe rear", v: nf(t.alignment.toeR, 1) + "°" },
        { k: "Caster", v: nf(t.alignment.caster, 1) + "°" }] },
    { id: "arb", title: "Anti-Roll Bars", icon: "🌀", rows: (t) => [
        { k: "Front", v: nf(t.arb.front, 0) }, { k: "Rear", v: nf(t.arb.rear, 0) }] },
    { id: "springs", title: "Springs & Ride Height", icon: "🔧", rows: (t) => [
        { k: "Front rate", v: springDisp(t.springs.front) + " " + (units === "metric" ? "kgf/mm" : "lb/in") },
        { k: "Rear rate", v: springDisp(t.springs.rear) + " " + (units === "metric" ? "kgf/mm" : "lb/in") },
        { k: "Ride height F", v: rideDisp(t.springs.rideF) + (units === "metric" ? " cm" : " in") },
        { k: "Ride height R", v: rideDisp(t.springs.rideR) + (units === "metric" ? " cm" : " in") }] },
    { id: "damping", title: "Damping", icon: "〽️", rows: (t) => [
        { k: "Rebound front", v: nf(t.damping.reboundF, 1) }, { k: "Rebound rear", v: nf(t.damping.reboundR, 1) },
        { k: "Bump front", v: nf(t.damping.bumpF, 1) }, { k: "Bump rear", v: nf(t.damping.bumpR, 1) }] },
    { id: "aero", title: "Aero", icon: "🪽", rows: (t) => !t.aero.applicable
        ? [{ k: "Aero", v: "Not installed" }]
        : [
            { k: "Front downforce", v: aeroVal(t.aero.front, t.aero.frontLbf, "— (no splitter)") },
            { k: "Rear downforce", v: aeroVal(t.aero.rear, t.aero.rearLbf, "— (no wing)") },
          ] },
    { id: "braking", title: "Braking", icon: "🛑", rows: (t) => [
        { k: "Balance (→front)", v: t.braking.balance + "%" }, { k: "Pressure", v: t.braking.pressure + "%" }] },
    { id: "differential", title: "Differential", icon: "🔩", rows: diffRows },
  ];

  /* ---------- render: single-goal cards ---------- */
  // `base` (optional) is the centered-dial baseline; rows that differ from it are
  // highlighted in place so dial effects are visible at a glance. The exact
  // before→after numbers live in the "What the sliders changed" panel.
  function renderCards(t, base) {
    $("summaryStrip").innerHTML = t.summary
      .map((c) => `<span class="chip">${c.k}: <b>${c.v}</b></span>`).join("");

    $("output").innerHTML = CARDS.map((c) => {
      const why = t[c.id].why;
      const liveRows = c.rows(t);
      const baseRows = base ? c.rows(base) : null;
      const rows = liveRows.map((row, i) => {
        const changed = baseRows && baseRows[i] && baseRows[i].v !== row.v;
        return `<div class="row ${row.sub ? "sub" : ""}${changed ? " changed" : ""}"><span class="k">${row.k}</span><span class="v">${row.v}</span></div>`;
      }).join("");
      const whyHtml = why ? `
        <details class="why">
          <summary>Why &amp; formula</summary>
          <div class="body">${why.text}${why.formula && why.formula !== "—"
            ? `<span class="formula">${escapeHtml(why.formula)}</span>` : ""}</div>
        </details>` : "";
      return `<div class="card"><h3><span class="ico">${c.icon}</span>${c.title}</h3>
        <div class="rows">${rows}</div>${whyHtml}</div>`;
    }).join("");
  }

  /* ---------- "What the sliders changed" diff (live vs centered baseline) ---------- */
  // which end of the car a row label refers to (for plain-language effects)
  function endOf(key) {
    if (/front/i.test(key) || /\bF$/.test(key)) return "front";
    if (/rear/i.test(key) || /\bR$/.test(key)) return "rear";
    return "";
  }
  // map a changed output row to a plain-language effect + direction (up/down)
  function effectPhrase(cardId, key, fromStr, toStr) {
    const f = parseFloat(fromStr), tv = parseFloat(toStr);
    const up = !(isFinite(f) && isFinite(tv)) || tv >= f;
    const end = endOf(key);
    let text;
    switch (cardId) {
      case "arb":
        text = `${up ? "stiffer" : "softer"} ${end} anti-roll bar`; break;
      case "springs":
        text = /rate/i.test(key)
          ? `${up ? "stiffer" : "softer"} ${end} springs`
          : `${up ? "higher" : "lower"} ${end} ride height`;
        break;
      case "damping":
        text = `${up ? "firmer" : "softer"} ${end} ${/bump/i.test(key) ? "bump" : "rebound"}`; break;
      case "braking":
        text = up ? "more front brake bias" : "more rear brake bias"; break;
      case "differential":
        if (/center/i.test(key)) text = up ? "more torque to the rear" : "more torque to the front";
        else text = `${up ? "more" : "less"} ${end ? end + " " : ""}${/accel/i.test(key) ? "accel" : "decel"} lock`;
        break;
      case "aero":
        text = `${up ? "more" : "less"} ${end} downforce`; break;
      default:
        text = `${key} ${up ? "increased" : "decreased"}`;
    }
    return { text, from: fromStr, to: toStr, dir: up ? "up" : "down" };
  }

  function renderChangesPanel(t, base) {
    const panel = $("sliderChanges");
    if (!base) { panel.hidden = true; panel.innerHTML = ""; return; }
    const items = [];
    for (const c of CARDS) {
      const liveRows = c.rows(t), baseRows = c.rows(base);
      liveRows.forEach((row, i) => {
        if (!baseRows[i] || baseRows[i].v === row.v) return;
        items.push(effectPhrase(c.id, row.k, baseRows[i].v, row.v));
      });
    }
    if (!items.length) { panel.hidden = true; panel.innerHTML = ""; return; }
    const dials = [];
    if (num("handlingBias") !== 0) dials.push("handling bias");
    if (num("overallStiffness") !== 0) dials.push("overall stiffness");
    panel.hidden = false;
    panel.innerHTML =
      `<div class="sc-head">` +
        `<h4 class="sc-title">What the sliders changed</h4>` +
        `<span class="sc-sub">${items.length} setting${items.length > 1 ? "s" : ""} moved vs the centered baseline · <b>${escapeHtml(dials.join(" + "))}</b></span>` +
      `</div>` +
      `<ul class="sc-list">` +
        items.map((it) =>
          `<li class="sc-item ${it.dir}"><span class="sc-eff">${escapeHtml(it.text)}</span>` +
          `<span class="sc-delta">${escapeHtml(it.from)} → ${escapeHtml(it.to)} <i class="arr">${it.dir === "up" ? "▲" : "▼"}</i></span></li>`
        ).join("") +
      `</ul>`;
  }

  /* ---------- render: compare table (all goals) ---------- */
  function compareRowDefs(input) {
    const n = input.gears;
    const ev = input.powertrain === "EV";
    const aeroFrontLbf = input.hasFrontAero && input.aeroFront[0] != null;
    const aeroRearLbf = input.hasRearAero && input.aeroRear[0] != null;
    const defs = [
      { group: "Tires" },
      { label: "Front psi", get: (t) => nf(t.tires.front, 1) },
      { label: "Rear psi", get: (t) => nf(t.tires.rear, 1) },
      { group: "Gearing" },
      { label: "Final drive", get: (t) => nf(t.gearing.final, 2) },
    ];
    if (!ev) for (let g = 0; g < n; g++) defs.push({ label: `Gear ${g + 1}`, get: (t) => t.gearing.ratios[g] != null ? nf(t.gearing.ratios[g], 2) : "—" });
    if (input.redlineRpm && input.tireDiameter) defs.push({ label: `Top speed (${units === "metric" ? "km/h" : "mph"})`, get: (t) => t.gearing.topSpeed != null ? nf(fromImp("speed", t.gearing.topSpeed), 0) : "—" });
    defs.push(
      { group: "Alignment" },
      { label: "Camber F", get: (t) => nf(t.alignment.camberF, 1) + "°" },
      { label: "Camber R", get: (t) => nf(t.alignment.camberR, 1) + "°" },
      { label: "Toe F", get: (t) => nf(t.alignment.toeF, 1) + "°" },
      { label: "Toe R", get: (t) => nf(t.alignment.toeR, 1) + "°" },
      { label: "Caster", get: (t) => nf(t.alignment.caster, 1) + "°" },
      { group: "Anti-Roll Bars" },
      { label: "ARB Front", get: (t) => nf(t.arb.front, 0) },
      { label: "ARB Rear", get: (t) => nf(t.arb.rear, 0) },
      { group: `Springs (${units === "metric" ? "kgf/mm" : "lb/in"})` },
      { label: "Front rate", get: (t) => springDisp(t.springs.front) },
      { label: "Rear rate", get: (t) => springDisp(t.springs.rear) },
      { label: `Ride F (${units === "metric" ? "cm" : "in"})`, get: (t) => rideDisp(t.springs.rideF) },
      { label: `Ride R (${units === "metric" ? "cm" : "in"})`, get: (t) => rideDisp(t.springs.rideR) },
      { group: "Damping" },
      { label: "Rebound F", get: (t) => nf(t.damping.reboundF, 1) },
      { label: "Rebound R", get: (t) => nf(t.damping.reboundR, 1) },
      { label: "Bump F", get: (t) => nf(t.damping.bumpF, 1) },
      { label: "Bump R", get: (t) => nf(t.damping.bumpR, 1) },
      { group: "Aero" },
      { label: aeroFrontLbf ? `Front DF (${units === "metric" ? "kgf" : "lbf"})` : "Front DF",
        get: (t) => (!t.aero.applicable || t.aero.front == null) ? "—" : (aeroFrontLbf && t.aero.frontLbf != null ? nf(fromImp("aero", t.aero.frontLbf), 0) : t.aero.front + "%") },
      { label: aeroRearLbf ? `Rear DF (${units === "metric" ? "kgf" : "lbf"})` : "Rear DF",
        get: (t) => (!t.aero.applicable || t.aero.rear == null) ? "—" : (aeroRearLbf && t.aero.rearLbf != null ? nf(fromImp("aero", t.aero.rearLbf), 0) : t.aero.rear + "%") },
      { group: "Braking" },
      { label: "Balance", get: (t) => t.braking.balance + "%" },
      { label: "Pressure", get: (t) => t.braking.pressure + "%" },
      { group: "Differential" }
    );
    if (input.drivetrain === "AWD") {
      defs.push(
        { label: "Front accel", get: (t) => (t.differential.frontAccel ?? "—") + "%" },
        { label: "Front decel", get: (t) => (t.differential.frontDecel ?? "—") + "%" },
        { label: "Rear accel", get: (t) => t.differential.accel + "%" },
        { label: "Rear decel", get: (t) => t.differential.decel + "%" },
        { label: "Center→rear", get: (t) => (t.differential.centerRear ?? "—") + "%" }
      );
    } else {
      defs.push(
        { label: "Accel lock", get: (t) => t.differential.accel + "%" },
        { label: "Decel lock", get: (t) => t.differential.decel + "%" }
      );
    }
    return defs;
  }

  function renderCompare(input) {
    const tunes = GOALS.map((g) => compute(input, g));
    const defs = compareRowDefs(input);
    const head = `<tr><th>Setting</th>${GOALS.map((g) => `<th>${GOAL_META[g].icon} ${GOAL_META[g].label}</th>`).join("")}</tr>`;
    const body = defs.map((d) => {
      if (d.group) return `<tr class="group"><td colspan="${GOALS.length + 1}">${d.group}</td></tr>`;
      const cells = tunes.map((t) => `<td class="num">${d.get(t)}</td>`).join("");
      return `<tr><td>${d.label}</td>${cells}</tr>`;
    }).join("");
    $("compareWrap").innerHTML = `<table class="compare"><thead>${head}</thead><tbody>${body}</tbody></table>`;
  }

  /* ---------- copy current tune as text ---------- */
  function tuneToText(t, input) {
    const L = [];
    L.push(`FH6 TUNE — ${GOAL_META[t.goal].label}`);
    L.push(`Car: ${input.drivetrain} ${input.engineLocation}-engine ${input.powertrain}, ${nf(t.derived.frac * 100, 0)}% front, P/W ${nf(t.derived.pw, 2)} hp/lb`);
    if (input.handlingBias !== 0 || input.overallStiffness !== 0) {
      const parts = [];
      if (input.handlingBias !== 0) parts.push(`bias ${input.handlingBias > 0 ? "+" : ""}${nf(input.handlingBias, 1)} (${input.handlingBias > 0 ? "oversteer" : "understeer"})`);
      if (input.overallStiffness !== 0) parts.push(`stiffness ${input.overallStiffness > 0 ? "+" : ""}${nf(input.overallStiffness, 1)} (${input.overallStiffness > 0 ? "hard" : "soft"})`);
      L.push(`Dials: ${parts.join(", ")}`);
    }
    L.push(`— Tires: F ${nf(t.tires.front, 1)} / R ${nf(t.tires.rear, 1)} psi`);
    L.push(`— Final drive: ${nf(t.gearing.final, 2)}${t.gearing.singleSpeed ? `  1st (only gear): ${nf(t.gearing.ratios[0], 2)}` : "  Gears: " + t.gearing.ratios.map((r) => nf(r, 2)).join(", ")}${t.gearing.topSpeed != null ? `  | Top speed ~${speedDisp(t.gearing.topSpeed)}` : ""}`);
    L.push(`— Camber: F ${nf(t.alignment.camberF, 1)} / R ${nf(t.alignment.camberR, 1)}  Toe: F ${nf(t.alignment.toeF, 1)} / R ${nf(t.alignment.toeR, 1)}  Caster: ${nf(t.alignment.caster, 1)}`);
    L.push(`— ARB: F ${t.arb.front} / R ${t.arb.rear}`);
    L.push(`— Springs: F ${springDisp(t.springs.front)} / R ${springDisp(t.springs.rear)} ${units === "metric" ? "kgf/mm" : "lb/in"}  Ride: F ${rideDisp(t.springs.rideF)} / R ${rideDisp(t.springs.rideR)} ${units === "metric" ? "cm" : "in"}`);
    L.push(`— Damping: Reb F ${nf(t.damping.reboundF, 1)} / R ${nf(t.damping.reboundR, 1)}  Bump F ${nf(t.damping.bumpF, 1)} / R ${nf(t.damping.bumpR, 1)}`);
    const af = t.aero.front == null ? "n/a" : (t.aero.frontLbf != null ? aeroDisp(t.aero.frontLbf) : t.aero.front + "%");
    const ar = t.aero.rear == null ? "n/a" : (t.aero.rearLbf != null ? aeroDisp(t.aero.rearLbf) : t.aero.rear + "%");
    L.push(`— Aero: ${!t.aero.applicable ? "none installed" : `F ${af} / R ${ar}`}`);
    L.push(`— Brakes: balance ${t.braking.balance}% front, pressure ${t.braking.pressure}%`);
    if (t.differential.driveline === "AWD")
      L.push(`— Diff: front ${t.differential.frontAccel}/${t.differential.frontDecel}%, rear ${t.differential.accel}/${t.differential.decel}%, center ${t.differential.centerRear}% rear`);
    else
      L.push(`— Diff: ${t.differential.driveline} accel ${t.differential.accel}% / decel ${t.differential.decel}%`);
    return L.join("\n");
  }

  /* ---------- state + wiring ---------- */
  let currentGoal = "Circuit";

  function buildGoalTabs() {
    $("goalTabs").innerHTML = GOALS.map((g) =>
      `<button role="tab" data-goal="${g}" class="${g === currentGoal ? "active" : ""}">${GOAL_META[g].icon} ${GOAL_META[g].label}</button>`
    ).join("");
    $("goalTabs").querySelectorAll("button").forEach((b) =>
      b.addEventListener("click", () => { currentGoal = b.dataset.goal; refresh(); }));
  }

  // show only the range fields for the wing(s) the car actually has
  function syncAeroFields() {
    const kit = $("aeroKit").value;
    const showFront = kit === "Front" || kit === "Full";
    const showRear = kit === "Rear" || kit === "Full";
    document.querySelectorAll(".aero-front-field").forEach((e) => (e.style.display = showFront ? "" : "none"));
    document.querySelectorAll(".aero-rear-field").forEach((e) => (e.style.display = showRear ? "" : "none"));
    $("aeroRanges").style.display = kit === "None" ? "none" : "";
    $("aeroHint").style.display = kit === "None" ? "none" : "";
  }

  function showErrors(errors) {
    // Surface validation errors instead of rendering a broken tune.
    $("output").hidden = false; $("summaryStrip").hidden = true;
    $("compareWrap").hidden = true; $("sliderChanges").hidden = true;
    $("output").innerHTML =
      `<div class="card error-card"><h3><span class="ico">⚠️</span>Check your inputs</h3>` +
      `<div class="rows">` +
      errors.map((e) => `<div class="row"><span class="k">•</span><span class="v">${escapeHtml(e)}</span></div>`).join("") +
      `</div></div>`;
  }

  // The three stats a tune can't be derived without. Until they're filled, a
  // first-time visitor sees a friendly welcome rather than a wall of red errors.
  const REQUIRED_FIELDS = ["power", "weight", "frontWeight"];
  function isIncomplete() {
    return REQUIRED_FIELDS.some((id) => $(id).value.trim() === "");
  }

  function showWelcome() {
    $("output").hidden = false; $("summaryStrip").hidden = true;
    $("compareWrap").hidden = true; $("sliderChanges").hidden = true;
    $("output").innerHTML =
      `<div class="welcome">` +
        `<div class="welcome-ico">🏎️</div>` +
        `<h3>Enter your car's stats to build a tune</h3>` +
        `<p>Start with at least <b>power</b>, <b>weight</b> and <b>front weight %</b> on the left — ` +
        `your full, math-derived tune appears here instantly, across all six goals.</p>` +
      `</div>`;
  }

  function refresh() {
    syncAeroFields();
    updateBiasLabel();
    updateStiffLabel();
    updateTireDiaOut();
    const compare = $("compareMode").checked;
    $("goalTabs").querySelectorAll("button").forEach((b) =>
      b.classList.toggle("active", b.dataset.goal === currentGoal));
    $("goalTabs").style.opacity = compare ? 0.45 : 1;

    // First visit / not enough entered yet: a friendly welcome, not red errors.
    if (isIncomplete()) { showWelcome(); return; }

    const input = readInputs();
    // Refuse to produce a tune from invalid input — show the problems instead.
    const check = validate(input);
    if (!check.valid) { showErrors(check.errors); return; }

    if (compare) {
      $("output").hidden = true; $("summaryStrip").hidden = true;
      $("sliderChanges").hidden = true;
      $("compareWrap").hidden = false;
      renderCompare(input);
    } else {
      $("output").hidden = false; $("summaryStrip").hidden = false;
      $("compareWrap").hidden = true;
      const live = compute(input, currentGoal);
      // Baseline = same car with BOTH dials centered. It's the reference the
      // "what changed" view (panel + inline markers) diffs against.
      const dialed = input.handlingBias !== 0 || input.overallStiffness !== 0;
      const base = dialed
        ? compute(Object.assign({}, input, { handlingBias: 0, overallStiffness: 0 }), currentGoal)
        : null;
      renderCards(live, base);
      renderChangesPanel(live, base);
    }
  }

  function setUnits(next) {
    if (next === units) return;
    // convert the unit-bound field values so the same physical car is preserved
    Object.keys(FIELD_DIM).forEach((id) => {
      const el = $(id);
      if (el.value.trim() === "") return; // leave optional blanks blank
      const dim = FIELD_DIM[id];
      const conv = next === "metric" ? (+el.value) / M2I[dim] : (+el.value) * M2I[dim];
      // round per dimension so round-trips stay clean (spring/weight whole in imperial)
      const dp = dim === "ride" ? 1 : dim === "spring" ? (next === "metric" ? 2 : 0) : 0;
      el.value = +conv.toFixed(dp);
    });
    units = next;
    // relabel unit hints
    document.querySelectorAll("#unitToggle button").forEach((b) =>
      b.classList.toggle("active", b.dataset.units === units));
    const lbl = UNIT_LABEL[units];
    $("powerUnit").textContent = lbl.power; $("torqueUnit").textContent = lbl.torque;
    $("weightUnit").textContent = lbl.weight;
    $("rhUnitFmin").textContent = lbl.ride; $("rhUnitFmax").textContent = lbl.ride;
    $("rhUnitRmin").textContent = lbl.ride; $("rhUnitRmax").textContent = lbl.ride;
    $("srUnitFmin").textContent = lbl.spring; $("srUnitFmax").textContent = lbl.spring;
    $("srUnitRmin").textContent = lbl.spring; $("srUnitRmax").textContent = lbl.spring;
    $("afUnit").textContent = lbl.aero; $("afUnit2").textContent = lbl.aero;
    $("arUnit").textContent = lbl.aero; $("arUnit2").textContent = lbl.aero;
    $("topSpeedUnit").textContent = lbl.speed;
    refresh();
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
  }

  /* ---------- saved setups: status + localStorage access ---------- */
  let setupStatusTimer = null;
  function setupStatus(msg, isError) {
    const el = $("setupStatus");
    el.textContent = msg;
    el.classList.toggle("error", !!isError);
    clearTimeout(setupStatusTimer);
    if (msg) setupStatusTimer = setTimeout(() => { el.textContent = ""; el.classList.remove("error"); }, 4000);
  }

  // localStorage can be unavailable (private mode) or unreadable — both
  // degrade to an empty db with a status note; the calculator keeps working.
  function loadSetupsDb() {
    let raw = null;
    try {
      raw = localStorage.getItem(SETUPS.STORAGE_KEY);
    } catch (e) {
      setupStatus("Browser storage unavailable — setups won't persist.", true);
      return SETUPS.emptyDb();
    }
    if (raw == null) return SETUPS.emptyDb();
    const res = SETUPS.parseDb(raw);
    if (!res.ok) {
      setupStatus("Stored setups were unreadable — starting fresh.", true);
      return SETUPS.emptyDb();
    }
    if (res.skipped > 0) setupStatus(`${res.skipped} stored setup${res.skipped === 1 ? "" : "s"} couldn't be kept and ${res.skipped === 1 ? "was" : "were"} dropped.`, true);
    return res.db;
  }

  function saveSetupsDb(db) {
    try {
      localStorage.setItem(SETUPS.STORAGE_KEY, SETUPS.serializeDb(db));
      return true;
    } catch (e) {
      setupStatus("Couldn't write browser storage — setups not saved.", true);
      return false;
    }
  }

  // Snapshot every input/select in the Car Setup panel (by element id), plus
  // units, goal and both dials — the "full picture" a saved setup captures.
  function snapshotSetup(name) {
    const fields = {};
    document.querySelectorAll(".inputs input, .inputs select").forEach((el) => {
      if (!el.id || el.closest("#setupsBlock")) return;
      fields[el.id] = el.value;
    });
    return {
      name,
      savedAt: new Date().toISOString(),
      units,
      goal: currentGoal,
      dials: { handlingBias: $("handlingBias").value, overallStiffness: $("overallStiffness").value },
      fields,
    };
  }

  function applySetup(s) {
    // Units first: setUnits converts the stale on-screen values, which is fine
    // because every panel field is overwritten right after; it also fixes the
    // unit labels and the toggle's active state.
    setUnits(s.units === "metric" ? "metric" : "imperial");
    Object.keys(s.fields).forEach((id) => {
      const el = $(id);
      // only fields that still exist, and only inside the Car Setup panel
      if (el && el.closest(".inputs") && !el.closest("#setupsBlock")) el.value = String(s.fields[id]);
    });
    if (s.dials.handlingBias != null) $("handlingBias").value = s.dials.handlingBias;
    if (s.dials.overallStiffness != null) $("overallStiffness").value = s.dials.overallStiffness;
    if (GOALS.includes(s.goal)) currentGoal = s.goal;
    refresh();
  }

  // Options are built via DOM (not innerHTML) so names with quotes are safe.
  function renderSetupList(db, selectedName) {
    const sel = $("setupList");
    sel.innerHTML = "";
    const sorted = [...db.setups].sort((a, b) => String(b.savedAt).localeCompare(String(a.savedAt)));
    if (!sorted.length) {
      const o = document.createElement("option");
      o.value = ""; o.disabled = true; o.selected = true;
      o.textContent = "— no saved setups —";
      sel.appendChild(o);
    }
    for (const s of sorted) {
      const o = document.createElement("option");
      o.value = s.name; o.textContent = s.name;
      if (s.name === selectedName) o.selected = true;
      sel.appendChild(o);
    }
    const has = sorted.length > 0;
    $("setupLoad").disabled = !has;
    $("setupDelete").disabled = !has;
    $("setupExport").disabled = !has;
  }

  function wireSetups() {
    renderSetupList(loadSetupsDb(), null);

    $("setupSave").addEventListener("click", () => {
      const name = $("setupName").value.trim();
      if (!name) { setupStatus("Give the setup a name first.", true); return; }
      const db = loadSetupsDb();
      if (db.setups.some((s) => s.name === name) && !window.confirm(`Overwrite the saved setup “${name}”?`)) return;
      const next = SETUPS.upsertSetup(db, snapshotSetup(name));
      if (saveSetupsDb(next)) { renderSetupList(next, name); setupStatus(`Saved “${name}” ✓`); }
    });

    $("setupList").addEventListener("change", () => {
      if ($("setupList").value) $("setupName").value = $("setupList").value;
    });

    $("setupLoad").addEventListener("click", () => {
      const name = $("setupList").value;
      const s = loadSetupsDb().setups.find((x) => x.name === name);
      if (!s) { setupStatus("Pick a setup to load.", true); return; }
      applySetup(s);
      $("setupName").value = s.name;
      setupStatus(`Loaded “${s.name}” ✓`);
    });

    $("setupDelete").addEventListener("click", () => {
      const name = $("setupList").value;
      if (!name) { setupStatus("Pick a setup to delete.", true); return; }
      if (!window.confirm(`Delete the saved setup “${name}”?`)) return;
      const next = SETUPS.deleteSetup(loadSetupsDb(), name);
      if (saveSetupsDb(next)) { renderSetupList(next, null); setupStatus(`Deleted “${name}” ✓`); }
    });
  }

  function init() {
    buildGoalTabs();
    // live updates on every input — except the setups controls, which manage
    // saved tunes rather than describing the car
    document.querySelectorAll("input, select").forEach((el) => {
      if (el.closest("#setupsBlock")) return;
      el.addEventListener("input", refresh);
      el.addEventListener("change", refresh);
    });
    document.querySelectorAll("#unitToggle button").forEach((b) =>
      b.addEventListener("click", () => setUnits(b.dataset.units)));
    $("compareMode").addEventListener("change", refresh);
    $("biasReset").addEventListener("click", () => { $("handlingBias").value = "0"; refresh(); });
    $("stiffReset").addEventListener("click", () => { $("overallStiffness").value = "0"; refresh(); });
    $("copyBtn").addEventListener("click", async () => {
      const input = readInputs();
      const text = $("compareMode").checked
        ? GOALS.map((g) => tuneToText(compute(input, g), input)).join("\n\n")
        : tuneToText(compute(input, currentGoal), input);
      try {
        await navigator.clipboard.writeText(text);
        const btn = $("copyBtn"); const old = btn.textContent;
        btn.textContent = "Copied ✓"; setTimeout(() => (btn.textContent = old), 1400);
      } catch (e) {
        // clipboard may be blocked on file://; fall back to a prompt
        window.prompt("Copy your tune:", text);
      }
    });
    wireSetups();
    refresh();
  }

  document.addEventListener("DOMContentLoaded", init);
})();



