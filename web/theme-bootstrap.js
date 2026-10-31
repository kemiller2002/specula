// Apply the saved appearance before any visible application content is
// parsed. F# remains the source of preference truth after Limen starts; this
// tiny bootstrap prevents a first-paint theme flash.
(() => {
  const themeRoot = document.currentScript?.parentElement;
  if (!themeRoot) return;

  let theme = "light";
  try {
    const saved = JSON.parse(localStorage.getItem("specula.preferences") || "null");
    if (saved?.theme === "dark" || saved?.theme === "light" || saved?.theme === "system") {
      theme = saved.theme;
    }
  } catch {
    // Corrupt or unavailable storage is handled by the F# preference loader.
  }
  const projectTheme = () => {
    const current = themeRoot.getAttribute("data-ef-theme") || "light";
    const brand = themeRoot.querySelector('[data-ef-brand="echelon"]');
    if (!brand) return;
    if (current === "light" || current === "dark") brand.setAttribute("data-ef-theme", current);
    else brand.removeAttribute("data-ef-theme");
  };

  themeRoot.setAttribute("data-ef-theme", theme);
  new MutationObserver(projectTheme).observe(themeRoot, {
    attributes: true,
    attributeFilter: ["data-ef-theme"],
    childList: true,
  });
  projectTheme();
})();
