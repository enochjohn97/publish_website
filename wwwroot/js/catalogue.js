/* Folio — live catalogue search */
(function () {
  "use strict";
  var input = document.getElementById("catalogue-search");
  var grid = document.getElementById("book-grid");
  var empty = document.getElementById("catalogue-empty");
  var emptyCopy = document.getElementById("catalogue-empty-copy");
  var count = document.getElementById("result-count");
  var note = document.getElementById("result-note");
  var errorLabel = document.getElementById("result-error");
  var spinner = document.getElementById("search-spinner");
  var toggle = document.getElementById("filter-toggle");
  var filters = document.getElementById("catalogue-filters");
  var category = document.getElementById("filter-category");
  var status = document.getElementById("filter-status");
  var sort = document.getElementById("sort-select");
  var filterCount = document.getElementById("filter-count");
  if (!input || !grid) return;

  var books = [];
  var timer = null;
  var controller = null;

  function esc(value) { return window.Folio.escapeHtml(value); }

  function coverMarkup(book) {
    if (book.coverImageUrl) {
      return '<img class="book-cover" src="' + esc(book.coverImageUrl) + '" alt="Cover of ' + esc(book.title) + '" loading="lazy">';
    }
    return '<div class="cover-fallback"><span class="cover-press">Folio Press</span><span class="cover-title">' +
      esc(book.title) + '</span><span class="cover-author">' + esc(book.authorName) + "</span></div>";
  }

  function cardMarkup(book) {
    var statusLabel = book.status === "completed" ? "Completed" : book.status === "ongoing" ? "Ongoing" : "Draft";
    var slug = encodeURIComponent(book.slug);
    return '<article class="book-card"><a href="/books/' + slug + '" aria-label="' + esc(book.title) + ' by ' + esc(book.authorName) + '">' +
      '<div class="book-card-cover">' + coverMarkup(book) + '<span class="cover-status">' + statusLabel + "</span></div></a>" +
      '<div class="book-card-body"><p class="book-category">' + esc(book.category) + '</p>' +
      '<h3 class="book-title"><a href="/books/' + slug + '">' + esc(book.title) + "</a></h3>" +
      '<p class="book-author">by ' + esc(book.authorName) + "</p>" +
      '<p class="book-synopsis">' + esc(book.synopsis) + "</p>" +
      '<p class="book-foot"><span>' + book.chapterCount + " " + (book.chapterCount === 1 ? "chapter" : "chapters") +
      '</span><span aria-hidden="true">·</span><span>' + book.readingMinutes + " min read</span>" +
      (book.contributorCount > 0 ? '<span aria-hidden="true">·</span><span>' + book.contributorCount + " contributors</span>" : "") +
      "</p></div></article>";
  }

  function applySort(list) {
    var copy = list.slice();
    if (sort && sort.value === "title") copy.sort(function (a, b) { return a.title.localeCompare(b.title); });
    else if (sort && sort.value === "chapters") copy.sort(function (a, b) { return b.chapterCount - a.chapterCount; });
    return copy;
  }

  function render() {
    var sorted = applySort(books);
    grid.innerHTML = sorted.map(cardMarkup).join("");
    if (empty) empty.classList.toggle("hidden", sorted.length > 0);
    if (emptyCopy) {
      emptyCopy.textContent = "Try a different keyword, or clear the genre and status filters to see the full catalogue.";
    }
    if (count) count.textContent = sorted.length + (sorted.length === 1 ? " title" : " titles");
    if (note) note.textContent = input.value.trim() ? "matching “" + input.value.trim() + "”" : "";
  }

  function search() {
    if (controller) controller.abort();
    controller = new AbortController();
    if (spinner) spinner.classList.remove("hidden");
    if (errorLabel) errorLabel.classList.add("hidden");

    var params = new URLSearchParams();
    if (input.value.trim()) params.set("q", input.value.trim());
    if (category && category.value !== "all") params.set("category", category.value);
    if (status && status.value !== "all") params.set("status", status.value);

    fetch("/api/books?" + params.toString(), { signal: controller.signal })
      .then(function (response) { return response.json(); })
      .then(function (payload) {
        if (!payload || !payload.ok) throw new Error("search failed");
        books = payload.data.books || [];
        render();
      })
      .catch(function (error) {
        if (error.name === "AbortError") return;
        if (errorLabel) errorLabel.classList.remove("hidden");
      })
      .finally(function () {
        if (spinner) spinner.classList.add("hidden");
      });
  }

  function refreshFilterCount() {
    var active = 0;
    if (category && category.value !== "all") active++;
    if (status && status.value !== "all") active++;
    if (filterCount) filterCount.textContent = active ? "(" + active + ")" : "";
    if (toggle) toggle.classList.toggle("btn-primary", active > 0);
  }

  input.addEventListener("input", function () {
    window.clearTimeout(timer);
    timer = window.setTimeout(search, input.value ? 260 : 0);
  });

  if (toggle && filters) {
    toggle.addEventListener("click", function () {
      var open = filters.hasAttribute("hidden");
      if (open) filters.removeAttribute("hidden");
      else filters.setAttribute("hidden", "");
      toggle.setAttribute("aria-expanded", open ? "true" : "false");
    });
  }

  [category, status].forEach(function (control) {
    if (control) control.addEventListener("change", function () { refreshFilterCount(); search(); });
  });

  if (sort) sort.addEventListener("change", render);

  // Seed with server-rendered books so no extra request is needed on load.
  document.querySelectorAll("#book-grid .book-card").forEach(function () { /* server cards already visible */ });
  books = Array.prototype.map.call(grid.querySelectorAll(".book-card"), function (card) {
    var link = card.querySelector(".book-title a");
    return {
      slug: link ? link.getAttribute("href").split("/").pop() : "",
      title: link ? link.textContent : "",
      authorName: "",
      coverImageUrl: card.querySelector("img.book-cover") ? card.querySelector("img.book-cover").getAttribute("src") : null,
      category: card.querySelector(".book-category") ? card.querySelector(".book-category").textContent : "",
      synopsis: card.querySelector(".book-synopsis") ? card.querySelector(".book-synopsis").textContent : "",
      chapterCount: 0,
      readingMinutes: 0,
      contributorCount: 0,
      status: "ongoing"
    };
  });
  refreshFilterCount();
})();
