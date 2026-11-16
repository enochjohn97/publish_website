/* Folio — shared UI utilities (toasts, modals, API, profile) */
(function () {
  "use strict";

  var csrfMeta = document.querySelector('meta[name="csrf-token"]');
  var csrfToken = csrfMeta ? csrfMeta.getAttribute("content") : "";

  function escapeHtml(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function parseDate(value) {
    if (!value) return null;
    var raw = String(value);
    if (!/Z$|[+-]\d{2}:?\d{2}$/.test(raw)) raw += "Z";
    var date = new Date(raw);
    return isNaN(date.getTime()) ? null : date;
  }

  function formatDate(value) {
    var date = parseDate(value);
    return date ? date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" }) : "—";
  }

  function formatDateTime(value) {
    var date = parseDate(value);
    if (!date) return "—";
    return formatDate(value) + " · " + date.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" });
  }

  function timeAgo(value) {
    var date = parseDate(value);
    if (!date) return "";
    var seconds = Math.round((Date.now() - date.getTime()) / 1000);
    if (seconds < 60) return "just now";
    var minutes = Math.round(seconds / 60);
    if (minutes < 60) return minutes + "m ago";
    var hours = Math.round(minutes / 60);
    if (hours < 24) return hours + "h ago";
    var days = Math.round(hours / 24);
    if (days < 30) return days + "d ago";
    return formatDate(value);
  }

  function initials(name) {
    return String(name || "")
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(function (part) { return part.charAt(0).toUpperCase(); })
      .join("");
  }

  function toast(kind, title, description) {
    var stack = document.getElementById("toast-stack");
    if (!stack) return;
    var el = document.createElement("div");
    el.className = "toast toast-" + (kind || "info");
    el.setAttribute("role", "status");
    var icon = kind === "success" ? "✓" : kind === "error" ? "!" : kind === "warning" ? "⚠" : "i";
    el.innerHTML =
      '<div style="flex:1"><strong>' + escapeHtml(title) + "</strong>" +
      (description ? "<p>" + escapeHtml(description) + "</p>" : "") +
      '</div><button type="button" aria-label="Dismiss notification">✕</button>';
    el.querySelector("button").addEventListener("click", function () { el.remove(); });
    el.insertAdjacentHTML("afterbegin", '<span aria-hidden="true" style="font-weight:700">' + icon + "</span>");
    stack.appendChild(el);
    window.setTimeout(function () { el.remove(); }, kind === "error" ? 7000 : 5000);
  }

  /* ------------------------------------------------------------ login quotes */
  var loginQuote = document.getElementById("login-quote");
  if (loginQuote) {
    var quoteSource = document.getElementById("login-quote-source");
    var quotes = [
      { q: "A reader lives a thousand lives before he dies.", a: "George R.R. Martin" },
      { q: "There is no friend as loyal as a book.", a: "Ernest Hemingway" },
      { q: "That is part of the beauty of all literature. You discover that your longings are universal longings.", a: "F. Scott Fitzgerald" },
      { q: "A word after a word after a word is power.", a: "Margaret Atwood" },
      { q: "We read to know we are not alone.", a: "C.S. Lewis" },
      { q: "Books are a uniquely portable magic.", a: "Stephen King" },
      { q: "Until I feared I would lose it, I never loved to read. One does not love breathing.", a: "Harper Lee" },
      { q: "A room without books is like a body without a soul.", a: "Cicero" },
      { q: "There is no charm equal to tenderness of heart.", a: "Jane Austen" },
      { q: "Fairy tales are more than true: not because they tell us dragons exist, but because they tell us dragons can be beaten.", a: "Neil Gaiman" },
      { q: "Words are, in my not-so-humble opinion, our most inexhaustible source of magic.", a: "J.K. Rowling" },
      { q: "The world is a book and those who do not travel read only one page.", a: "Augustine of Hippo" },
      { q: "I cannot live without books.", a: "Thomas Jefferson" },
      { q: "A book must be the axe for the frozen sea within us.", a: "Franz Kafka" },
      { q: "Reading is a conversation. All books talk. But a good book listens as well.", a: "Mark Haddon" },
      { q: "Books are mirrors: you only see in them what you already have inside you.", a: "Carlos Ruiz Zafón" },
      { q: "There are worse crimes than burning books. One of them is not reading them.", a: "Joseph Brodsky" },
      { q: "A writer only begins a book. A reader finishes it.", a: "Samuel Johnson" },
      { q: "If you only read the books that everyone else is reading, you can only think what everyone else is thinking.", a: "Haruki Murakami" },
      { q: "Reading gives us someplace to go when we have to stay where we are.", a: "Mason Cooley" },
      { q: "The person who deserves most pity is a lonesome one on a rainy day who doesn't know how to read.", a: "Benjamin Franklin" },
      { q: "Books are the quietest and most constant of friends.", a: "Charles William Eliot" },
      { q: "Stories are equipment for living.", a: "Kenneth Burke" },
      { q: "I have always imagined that Paradise will be a kind of library.", a: "Jorge Luis Borges" },
      { q: "It is what you read when you don't have to that determines what you will be when you can't help it.", a: "Oscar Wilde" },
      { q: "A book is a dream that you hold in your hands.", a: "Neil Gaiman" },
      { q: "The reading of all good books is like a conversation with the finest minds of past centuries.", a: "René Descartes" },
      { q: "No two persons ever read the same book.", a: "Edmund Wilson" },
      { q: "Read the books you love, tell people about them, and keep books in your life.", a: "Karen Joy Fowler" },
      { q: "Once you learn to read, you will be forever free.", a: "Frederick Douglass" },
      { q: "A reader's heart is a house with many doors.", a: "Folio Press" },
      { q: "Every page turned is a door opened.", a: "Folio Press" }
    ];
    var quoteWindow = 3 * 60 * 1000;
    var quoteIndex = function () {
      var slot = Math.floor(Date.now() / quoteWindow);
      return ((slot % quotes.length) + quotes.length) % quotes.length;
    };
    var activeQuote = quoteIndex();
    function paintQuote(animate) {
      var entry = quotes[activeQuote];
      if (!animate) {
        loginQuote.textContent = "“" + entry.q + "”";
        if (quoteSource) quoteSource.textContent = "— " + entry.a;
        return;
      }
      loginQuote.style.opacity = "0";
      window.setTimeout(function () {
        loginQuote.textContent = "“" + entry.q + "”";
        if (quoteSource) quoteSource.textContent = "— " + entry.a;
        loginQuote.style.opacity = "1";
      }, 260);
    }
    paintQuote(false);
    window.setInterval(function () {
      var next = quoteIndex();
      if (next === activeQuote) return;
      activeQuote = next;
      paintQuote(true);
    }, 15000);
  }

  function api(path, options) {
    options = options || {};
    var method = options.method || (options.json || options.body ? "POST" : "GET");
    var headers = { RequestVerificationToken: csrfToken };
    var body;
    if (options.json !== undefined) {
      headers["Content-Type"] = "application/json";
      body = JSON.stringify(options.json);
    } else if (options.body) {
      body = options.body;
    }
    return fetch(path, { method: method, headers: headers, body: body, credentials: "same-origin" })
      .then(function (response) {
        return response.json().catch(function () { return null; }).then(function (payload) {
          if (!response.ok || !payload || payload.ok === false) {
            var error = (payload && payload.error) || {};
            var failure = new Error(error.message || "Something went wrong. Please try again.");
            failure.status = response.status;
            failure.code = error.code || "INTERNAL_ERROR";
            failure.fields = error.fields || {};
            throw failure;
          }
          return payload.data;
        });
      })
      .catch(function (error) {
        if (error && error.status === undefined) {
          var offline = new Error("We couldn't reach the server. Check your connection and try again.");
          offline.status = 0;
          throw offline;
        }
        throw error;
      });
  }

  var activeModal = null;

  function openModal(modal) {
    if (!modal) return;
    modal.hidden = false;
    activeModal = modal;
    document.body.style.overflow = "hidden";
    var focusTarget = modal.querySelector("[data-autofocus]");
    window.setTimeout(function () {
      (focusTarget || modal.querySelector(".modal-panel")).focus();
    }, 40);
  }

  function closeModal(modal) {
    var target = modal || activeModal;
    if (!target) return;
    target.hidden = true;
    if (activeModal === target) activeModal = null;
    if (!document.querySelector(".modal:not([hidden])")) document.body.style.overflow = "";
  }

  document.addEventListener("click", function (event) {
    var closeTrigger = event.target.closest("[data-modal-close]");
    if (closeTrigger) {
      closeModal(closeTrigger.closest(".modal"));
    }
  });

  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape" && activeModal) closeModal(activeModal);
  });

  document.querySelectorAll("[data-signin-open]").forEach(function (button) {
    button.addEventListener("click", function () {
      openModal(document.getElementById("signin-modal"));
    });
  });

  if (new URLSearchParams(window.location.search).get("signin") === "1") {
    openModal(document.getElementById("signin-modal"));
  }

  /* ------------------------------------------------------------ profile modal */
  function renderProfile(profile) {
    var body = document.getElementById("profile-body");
    var name = document.getElementById("profile-name");
    if (!body) return;

    var user = profile.user || {};
    if (name) name.textContent = user.username || "Your reader profile";

    var avatar = user.avatarUrl
      ? '<img class="profile-avatar" src="' + escapeHtml(user.avatarUrl) + '" alt="" referrerpolicy="no-referrer">'
      : '<div class="profile-avatar-fallback">' + escapeHtml(initials(user.username)) + "</div>";

    var html = '<div class="profile-head">' + avatar +
      '<div><p class="profile-name">' + escapeHtml(user.username) + "</p>" +
      '<p class="profile-email">' + escapeHtml(user.email) + "</p>" +
      '<div class="profile-chips">' +
      '<span class="chip">' + escapeHtml(user.role) + "</span>" +
      '<span class="chip chip-gold">' + escapeHtml(user.reputationScore) + " reputation</span>" +
      '<span class="chip chip-burgundy">' + escapeHtml((profile.comments || []).length) + " notes</span>" +
      '<span class="chip">Joined ' + escapeHtml(formatDate(user.createdAt)) + "</span>" +
      "</div></div></div>";

    html += '<div class="profile-section"><h3>Badges earned</h3>';
    if (!profile.badges || !profile.badges.length) {
      html += '<p class="profile-empty">No badges yet. Accepted notes and credited contributions earn badges.</p>';
    } else {
      html += '<div class="badge-grid">';
      profile.badges.forEach(function (badge) {
        html += '<div class="badge-item"><span class="badge-icon" aria-hidden="true">✦</span><div>' +
          "<strong>" + escapeHtml(badge.name) + "</strong><span>" + escapeHtml(badge.description) + "</span></div></div>";
      });
      html += "</div>";
    }
    html += "</div>";

    if (profile.contributions && profile.contributions.length) {
      html += '<div class="profile-section"><h3>Credited in</h3><ul class="profile-list">';
      profile.contributions.forEach(function (item) {
        html += "<li><span>" + escapeHtml(item.bookTitle) + "</span><span class=\"muted\">" +
          escapeHtml(formatDate(item.creditedAt)) + "</span></li>";
      });
      html += "</ul></div>";
    }

    html += '<div class="profile-section"><h3>Reading history</h3>';
    if (!profile.readingHistory || !profile.readingHistory.length) {
      html += '<p class="profile-empty">Nothing tracked yet. Open any chapter and your progress appears here.</p>';
    } else {
      html += '<ul class="profile-list">';
      profile.readingHistory.forEach(function (item) {
        html += '<li style="display:block"><div class="row" style="justify-content:space-between">' +
          '<a href="/books/' + encodeURIComponent(item.bookSlug) + "/" + encodeURIComponent(item.chapterNumber) + '">' +
          escapeHtml(item.bookTitle) + " · Ch. " + escapeHtml(item.chapterNumber) + "</a>" +
          '<span class="muted tiny">' + escapeHtml(timeAgo(item.updatedAt)) + "</span></div>" +
          '<div class="progress-track"><div class="progress-fill" style="width:' + Math.max(4, Math.min(100, item.progress || 0)) + '%"></div></div></li>';
      });
      html += "</ul>";
    }
    html += "</div>";

    html += '<div class="profile-section"><h3>Your notes &amp; contributions</h3>';
    if (!profile.comments || !profile.comments.length) {
      html += '<p class="profile-empty">You haven\'t left a note yet. Highlight a line while reading to share one.</p>';
    } else {
      html += '<div class="stack" style="gap:8px">';
      profile.comments.slice(0, 6).forEach(function (comment) {
        var tone = comment.status === "accepted" ? "badge-forest" : comment.status === "rejected" ? "badge-burgundy" : "badge-gold";
        var label = comment.status === "accepted" ? "Published" : comment.status === "rejected" ? "Not published" : "In review";
        html += '<div class="profile-note"><div class="profile-note-head"><span>' + escapeHtml(comment.bookTitle) +
          '</span><span class="badge-soft ' + tone + '">' + label + "</span></div><p>" +
          escapeHtml(comment.content) + "</p><time>" + escapeHtml(timeAgo(comment.createdAt)) + "</time></div>";
      });
      html += "</div>";
    }
    html += "</div>";

    body.innerHTML = html;
  }

  document.querySelectorAll("[data-profile-open]").forEach(function (button) {
    button.addEventListener("click", function () {
      var modal = document.getElementById("profile-modal");
      if (!modal) return;
      openModal(modal);
      var body = document.getElementById("profile-body");
      body.innerHTML = '<div class="skeleton skeleton-block"></div><div class="skeleton skeleton-block"></div>';
      api("/api/profile")
        .then(renderProfile)
        .catch(function (error) {
          if (error.status === 401) {
            window.location.href = "/?signin=1";
            return;
          }
          body.innerHTML = '<p class="profile-empty">We couldn\'t load your profile. Close this dialog and try again.</p>';
        });
    });
  });

  /* ------------------------------------------------------------ flash messages */
  var body = document.body;
  var notice = body.getAttribute("data-notice");
  var flashError = body.getAttribute("data-error");
  if (notice === "signed-in") toast("success", "Welcome back", "You're signed in with Google.");
  if (notice === "signed-out") toast("success", "Signed out", "You've been securely signed out.");
  if (notice === "admin-signed-in") toast("success", "Welcome back, editor", "You're signed in to the editorial desk.");
  if (notice === "book-published") toast("success", "Book created", "The new title is now available in the dashboard.");
  document.querySelectorAll("[data-toast-error]").forEach(function (el) {
    var message = el.getAttribute("data-toast-error");
    if (message) toast("error", "Sign-in problem", message);
  });
  if (flashError) {
    var messages = {
      google_not_configured: "Google sign-in isn't configured on this server yet.",
      google_denied: "Google sign-in was cancelled. You can try again any time.",
      google_state_mismatch: "That sign-in link expired. Please start again.",
      google_sign_in_failed: "We couldn't complete Google sign-in. Please try again.",
      google_start_failed: "We couldn't reach Google. Please try again.",
      forbidden: "That area is limited to editors.",
      admin_only: "The editorial dashboard is restricted to press editors."
    };
    toast("error", "Sign-in problem", messages[flashError] || "Something interrupted sign-in. Please try again.");
  }

  /* ------------------------------------------------------------ notifications */
  var bell = document.querySelector("[data-notifications-open]");
  var notificationPanel = document.getElementById("notification-panel");
  var notificationList = document.getElementById("notification-list");
  var notificationCount = document.getElementById("notification-count");
  var seenNotifications = {};
  var notificationsPrimed = false;

  function setUnread(count) {
    if (!notificationCount) return;
    var value = Math.max(0, Number(count) || 0);
    notificationCount.textContent = value > 99 ? "99+" : String(value);
    notificationCount.hidden = value === 0;
  }

  function renderNotifications(items) {
    if (!notificationList) return;
    if (!items.length) {
      notificationList.innerHTML = '<p class="notification-empty">Nothing yet. We\'ll let you know when something happens.</p>';
      return;
    }
    notificationList.innerHTML = items.map(function (item) {
      var href = item.url ? escapeHtml(item.url) : "#";
      return '<a class="notification-item' + (item.isRead ? "" : " is-unread") + '" href="' + href + '">' +
        '<span class="notification-dot" aria-hidden="true"></span>' +
        "<div><strong>" + escapeHtml(item.title) + "</strong><p>" + escapeHtml(item.body) +
        "</p><time>" + escapeHtml(timeAgo(item.createdAt)) + "</time></div></a>";
    }).join("");
  }

  function loadNotifications(poll) {
    if (!bell) return;
    api("/api/notifications").then(function (data) {
      var items = data.notifications || [];
      setUnread(data.unread);
      renderNotifications(items);

      if (notificationsPrimed && poll) {
        items.some(function (item) {
          if (seenNotifications[item.id]) return false;
          seenNotifications[item.id] = true;
          if (item.isRead) return false;
          toast("info", item.title, item.body);
          return true;
        });
      }
      items.forEach(function (item) { seenNotifications[item.id] = true; });
      notificationsPrimed = true;
    }).catch(function () { /* the bell simply stays quiet */ });
  }

  if (bell && notificationPanel) {
    bell.addEventListener("click", function () {
      var open = notificationPanel.hidden;
      notificationPanel.hidden = !open;
      bell.setAttribute("aria-expanded", open ? "true" : "false");
      if (!open) return;
      loadNotifications(false);
      api("/api/notifications/read", { method: "POST", json: {} })
        .then(function (data) { setUnread(data.unread); })
        .catch(function () { /* keep the list visible */ });
    });

    document.addEventListener("click", function (event) {
      if (notificationPanel.hidden) return;
      if (event.target.closest("#notification-panel") || event.target.closest("[data-notifications-open]")) return;
      notificationPanel.hidden = true;
      bell.setAttribute("aria-expanded", "false");
    });

    var markRead = document.getElementById("notification-mark-read");
    if (markRead) {
      markRead.addEventListener("click", function () {
        api("/api/notifications/read", { method: "POST", json: {} })
          .then(function () {
            setUnread(0);
            notificationList.querySelectorAll(".is-unread").forEach(function (item) { item.classList.remove("is-unread"); });
            toast("success", "All caught up", "Every notification is marked as read.");
          })
          .catch(function (error) { toast("error", "Couldn't update notifications", error.message); });
      });
    }

    loadNotifications(false);
    window.setInterval(function () { loadNotifications(true); }, 60000);
  }

  window.Folio = {
    api: api,
    toast: toast,
    escapeHtml: escapeHtml,
    formatDate: formatDate,
    formatDateTime: formatDateTime,
    timeAgo: timeAgo,
    initials: initials,
    openModal: openModal,
    closeModal: closeModal
  };
})();
