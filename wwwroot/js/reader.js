/* Folio — immersive reader: themes, typography, synced narration, marginalia */
(function () {
  "use strict";

  var Folio = window.Folio;
  var shell = document.getElementById("reader-shell");
  if (!shell) return;

  var chapterId = shell.getAttribute("data-chapter-id");
  var signedIn = document.body.getAttribute("data-signed-in") === "true";
  var textRegion = document.getElementById("reader-text");
  var blocks = Array.prototype.slice.call(document.querySelectorAll(".reader-block"));
  var modal = document.getElementById("note-modal");
  var form = document.getElementById("note-form");
  var contentField = document.getElementById("note-content");
  var quoteBox = document.getElementById("note-quote");
  var errorLabel = document.getElementById("note-error");
  var countLabel = document.getElementById("note-count");
  var selectionPop = document.getElementById("selection-pop");
  var playButton = document.getElementById("tts-play");
  var rateSelect = document.getElementById("tts-rate");

  var pendingSelection = null;
  var replyTarget = null;
  var prefs = { theme: "light", fontSize: 19, serif: true, margins: true, rate: 1, mode: "pages" };

  /* ---------------------------------------------------------- preferences */
  try {
    var stored = JSON.parse(localStorage.getItem("folio-reader-prefs") || "{}");
    Object.keys(prefs).forEach(function (key) {
      if (stored[key] !== undefined) prefs[key] = stored[key];
    });
  } catch (error) { /* ignore malformed preferences */ }

  function savePrefs() {
    localStorage.setItem("folio-reader-prefs", JSON.stringify(prefs));
  }  var pageSections = Array.prototype.slice.call(document.querySelectorAll(".reader-page"));
  var pageNav = document.getElementById("page-nav");
  var pageIndicator = document.getElementById("page-indicator");
  var pagePrev = document.getElementById("page-prev");
  var pageNext = document.getElementById("page-next");
  var currentPage = 1;
  var pageCount = pageSections.length || 1;
  var articleStart = document.querySelector(".reader-head");

  function setPage(number, scroll) {
    currentPage = Math.min(Math.max(1, number), pageCount);
    pageSections.forEach(function (section) {
      var isCurrent = Number(section.getAttribute("data-page")) === currentPage;
      if (prefs.mode === "pages" && !isCurrent) section.setAttribute("hidden", "");
      else section.removeAttribute("hidden");
    });
    if (pageIndicator) pageIndicator.textContent = "Page " + currentPage + " of " + pageCount;
    if (pagePrev) pagePrev.disabled = currentPage <= 1;
    if (pageNext) pageNext.disabled = currentPage >= pageCount;
    if (scroll && articleStart) articleStart.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  function applyLayoutMode() {
    if (prefs.mode === "pages") {
      if (pageNav) pageNav.hidden = false;
      setPage(currentPage, false);
    } else {
      if (pageNav) pageNav.hidden = true;
      pageSections.forEach(function (section) { section.removeAttribute("hidden"); });
    }
    document.querySelectorAll("[data-mode-set]").forEach(function (button) {
      button.setAttribute("aria-pressed", button.getAttribute("data-mode-set") === prefs.mode ? "true" : "false");
    });
  }

  function applyPrefs()
  {
    shell.setAttribute("data-theme", prefs.theme);
    document.querySelectorAll("[data-theme-set]").forEach(function (button) {
      button.setAttribute("aria-pressed", button.getAttribute("data-theme-set") === prefs.theme ? "true" : "false");
    });
    if (textRegion) {
      textRegion.style.fontSize = prefs.fontSize + "px";
      textRegion.style.lineHeight = prefs.serif ? "1.75" : "1.65";
      textRegion.classList.toggle("sans", !prefs.serif);
    }
    document.querySelectorAll("[data-serif-toggle]").forEach(function (button) {
      button.setAttribute("aria-pressed", prefs.serif ? "true" : "false");
      button.textContent = prefs.serif ? "Serif" : "Sans";
    });
    var layout = document.getElementById("reader-layout");
    var marginButton = document.querySelector("[data-margin-toggle]");
    if (layout) layout.classList.toggle("with-margins", prefs.margins);
    if (marginButton) marginButton.setAttribute("aria-pressed", prefs.margins ? "true" : "false");
    if (rateSelect) rateSelect.value = String(prefs.rate);
    applyLayoutMode();
  }

  document.querySelectorAll("[data-mode-set]").forEach(function (button) {
    button.addEventListener("click", function () {
      prefs.mode = button.getAttribute("data-mode-set") === "scroll" ? "scroll" : "pages";
      savePrefs();
      applyPrefs();
    });
  });

  if (pagePrev) pagePrev.addEventListener("click", function () { setPage(currentPage - 1, true); });
  if (pageNext) pageNext.addEventListener("click", function () { setPage(currentPage + 1, true); });

  document.querySelectorAll("[data-theme-set]").forEach(function (button) {
    button.addEventListener("click", function () {
      prefs.theme = button.getAttribute("data-theme-set");
      savePrefs();
      applyPrefs();
    });
  });

  document.querySelectorAll("[data-font]").forEach(function (button) {
    button.addEventListener("click", function () {
      var delta = button.getAttribute("data-font") === "increase" ? 1 : -1;
      prefs.fontSize = Math.min(30, Math.max(15, prefs.fontSize + delta));
      savePrefs();
      applyPrefs();
    });
  });

  document.querySelectorAll("[data-serif-toggle]").forEach(function (button) {
    button.addEventListener("click", function () {
      prefs.serif = !prefs.serif;
      savePrefs();
      applyPrefs();
    });
  });

  document.querySelectorAll("[data-margin-toggle]").forEach(function (button) {
    button.addEventListener("click", function () {
      prefs.margins = !prefs.margins;
      savePrefs();
      applyPrefs();
    });
  });

  applyPrefs();

  /* ---------------------------------------------------------- narration */
  var sentences = [];
  blocks.forEach(function (block, blockIndex) {
    var raw = block.getAttribute("data-raw-text");
    if (!raw) {
      var pill = block.querySelector(".note-pill");
      var clone = block.cloneNode(true);
      if (pill && clone.contains(pill)) clone.querySelector(".note-pill").remove();
      raw = clone.textContent.trim();
      block.setAttribute("data-raw-text", raw);
    }
    var parts = raw.match(/[^.!?…]+[.!?…]+["'”’)]?|[^.!?…]+$/g) || [raw];
    parts.forEach(function (sentence, sentenceIndex) {
      var text = sentence.trim();
      if (text) sentences.push({ text: text, blockIndex: blockIndex, sentenceIndex: sentenceIndex });
    });
  });

  var speech = window.speechSynthesis;
  var speakToken = 0;
  var currentIndex = 0;
  var isPaused = false;
  var isPlaying = false;

  function highlightBlock(blockIndex) {
    blocks.forEach(function (block, index) {
      block.classList.toggle("reader-block-active", index === blockIndex);
    });
    var activeBlock = blocks[blockIndex];
    if (!activeBlock) return;
    var blockPage = Number(activeBlock.getAttribute("data-page") || "1");
    if (prefs.mode === "pages" && blockPage !== currentPage) setPage(blockPage, false);
    activeBlock.scrollIntoView({ behavior: "smooth", block: "center" });
  }

  function setPlayLabel(label) {
    if (playButton) playButton.textContent = label;
  }

  function stopNarration(reset) {
    speakToken++;
    if (speech) speech.cancel();
    isPlaying = false;
    isPaused = false;
    setPlayLabel("▶ Listen");
    if (reset) {
      currentIndex = 0;
      blocks.forEach(function (block) { block.classList.remove("reader-block-active"); });
    }
  }

  function speakAt(index) {
    if (!speech) {
      Folio.toast("error", "Audio isn't available", "This browser doesn't support speech synthesis. Try Chrome, Edge or Safari.");
      return;
    }
    if (index >= sentences.length) {
      stopNarration(true);
      return;
    }
    var sentence = sentences[index];
    var utterance = new SpeechSynthesisUtterance(sentence.text);
    utterance.rate = Number(prefs.rate) || 1;
    utterance.lang = "en-US";

    var voices = speech.getVoices();
    var voice = voices.find(function (item) { return item.lang && item.lang.indexOf("en") === 0 && /natural|premium|enhanced/i.test(item.name); })
      || voices.find(function (item) { return item.lang && item.lang.indexOf("en") === 0; });
    if (voice) utterance.voice = voice;

    var token = ++speakToken;
    speech.cancel();
    isPlaying = true;
    isPaused = false;
    currentIndex = index;
    setPlayLabel("⏸ Pause");
    highlightBlock(sentence.blockIndex);

    utterance.onend = function () {
      if (token !== speakToken) return;
      speakAt(index + 1);
    };
    utterance.onerror = function () {
      if (token !== speakToken) return;
      stopNarration(false);
      setPlayLabel("▶ Listen");
    };
    speech.speak(utterance);
  }

  if (playButton) {
    playButton.addEventListener("click", function () {
      if (!speech) {
        Folio.toast("error", "Audio isn't available", "This browser doesn't support speech synthesis.");
        return;
      }
      if (isPlaying) {
        speech.pause();
        isPlaying = false;
        isPaused = true;
        setPlayLabel("▶ Resume");
        return;
      }
      if (isPaused) {
        speech.resume();
        isPlaying = true;
        isPaused = false;
        setPlayLabel("⏸ Pause");
        return;
      }
      speakAt(currentIndex);
    });
  }

  var stopButton = document.getElementById("tts-stop");
  if (stopButton) stopButton.addEventListener("click", function () { stopNarration(true); });

  var prevButton = document.getElementById("tts-prev");
  if (prevButton) {
    prevButton.addEventListener("click", function () {
      stopNarration(false);
      speakAt(Math.max(0, currentIndex - 1));
    });
  }

  var nextButton = document.getElementById("tts-next");
  if (nextButton) {
    nextButton.addEventListener("click", function () {
      stopNarration(false);
      speakAt(Math.min(sentences.length - 1, currentIndex + 1));
    });
  }

  if (rateSelect) {
    rateSelect.addEventListener("change", function () {
      prefs.rate = Number(rateSelect.value) || 1;
      savePrefs();
      var wasPlaying = isPlaying;
      stopNarration(false);
      if (wasPlaying) speakAt(currentIndex);
    });
  }

  window.addEventListener("beforeunload", function () { if (speech) speech.cancel(); });

  /* ---------------------------------------------------------- annotations */
  function openComposer(options) {
    if (!signedIn) {
      window.location.href = "/?signin=1&next=" + encodeURIComponent(window.location.pathname);
      return;
    }
    options = options || {};
    pendingSelection = options.paragraphId ? { paragraphId: options.paragraphId, text: options.quote || "" } : null;
    replyTarget = options.parentId || null;

    var kindLabel = document.getElementById("note-page-hint");
    if (kindLabel) {
      kindLabel.textContent = prefs.mode === "pages"
        ? "Attached to page " + currentPage + " of " + pageCount + "."
        : "Attached to this chapter.";
    }
    var defaultKind = document.querySelector('input[name="note-kind"][value="comment"]');
    if (defaultKind) defaultKind.checked = true;

    if (quoteBox) {
      if (options.quote) {
        quoteBox.textContent = "“" + options.quote + "”";
        quoteBox.hidden = false;
      } else {
        quoteBox.hidden = true;
      }
    }
    if (contentField) contentField.value = "";
    if (errorLabel) { errorLabel.hidden = true; errorLabel.textContent = ""; }
    if (countLabel) countLabel.textContent = "0/2000";
    var title = document.getElementById("note-modal-title");
    if (title) title.textContent = replyTarget ? "Reply to a note" : "Leave a note in the margin";
    document.getElementById("note-contribution").checked = false;
    document.getElementById("note-spoiler").checked = false;
    Folio.openModal(modal);
  }

  document.querySelectorAll("[data-compose-open]").forEach(function (button) {
    button.addEventListener("click", function () { openComposer({}); });
  });

  document.querySelectorAll("[data-reply]").forEach(function (button) {
    button.addEventListener("click", function () {
      var commentId = button.getAttribute("data-reply");
      var card = document.getElementById("comment-" + commentId);
      var quote = card ? card.querySelector(".note-quote") : null;
      openComposer({ parentId: commentId, quote: quote ? quote.textContent.trim() : "" });
    });
  });

  if (textRegion) {
    textRegion.addEventListener("mouseup", function () {
      var selection = window.getSelection();
      if (!selection || selection.isCollapsed) {
        if (selectionPop) selectionPop.hidden = true;
        return;
      }
      var text = selection.toString().trim();
      if (text.length < 2 || text.length > 400) {
        if (selectionPop) selectionPop.hidden = true;
        return;
      }
      var node = selection.anchorNode;
      var element = node && node.nodeType === 1 ? node : node && node.parentElement;
      var block = element ? element.closest(".reader-block") : null;
      if (!block) {
        if (selectionPop) selectionPop.hidden = true;
        return;
      }
      pendingSelection = { paragraphId: block.getAttribute("data-block-id"), text: text };
      if (selectionPop) {
        var rect = selection.getRangeAt(0).getBoundingClientRect();
        selectionPop.hidden = false;
        selectionPop.style.top = Math.max(80, rect.top - 46) + "px";
        selectionPop.style.left = Math.min(Math.max(90, rect.left + rect.width / 2), window.innerWidth - 90) + "px";
      }
    });
  }

  if (selectionPop) {
    selectionPop.addEventListener("click", function () {
      if (!pendingSelection) return;
      selectionPop.hidden = true;
      openComposer({ paragraphId: pendingSelection.paragraphId, quote: pendingSelection.text });
    });
  }

  document.addEventListener("mousedown", function (event) {
    if (selectionPop && !selectionPop.hidden && !selectionPop.contains(event.target) && !(textRegion && textRegion.contains(event.target))) {
      selectionPop.hidden = true;
    }
  });

  if (contentField && countLabel) {
    contentField.addEventListener("input", function () {
      countLabel.textContent = contentField.value.length + "/2000";
      if (errorLabel && !errorLabel.hidden) {
        errorLabel.hidden = true;
        contentField.classList.remove("input-error");
      }
    });
  }

  if (form) {
    form.addEventListener("submit", function (event) {
      event.preventDefault();
      var content = contentField.value.trim();
      if (content.length < 2) {
        if (errorLabel) { errorLabel.hidden = false; errorLabel.textContent = "Write at least a couple of words."; }
        contentField.classList.add("input-error");
        contentField.focus();
        return;
      }
      var submitButton = document.getElementById("note-submit");
      submitButton.disabled = true;
      submitButton.innerHTML = '<span class="spinner"></span> Sending…';

      var kindInput = document.querySelector('input[name="note-kind"]:checked');
      Folio.api("/api/comments", {
        json: {
          chapterId: chapterId,
          content: content,
          parentId: replyTarget,
          paragraphId: pendingSelection ? pendingSelection.paragraphId : null,
          pageNumber: prefs.mode === "pages" ? currentPage : null,
          kind: kindInput ? kindInput.value : "comment",
          highlightedText: pendingSelection ? pendingSelection.text : null,
          isSpoiler: document.getElementById("note-spoiler").checked,
          isContributorSubmission: document.getElementById("note-contribution").checked
        }
      })
        .then(function (data) {
          Folio.closeModal(modal);
          Folio.toast("success", "Note submitted", data.message || "Your note is with our editors for review.");
          form.reset();
          pendingSelection = null;
          replyTarget = null;
        })
        .catch(function (error) {
          if (error.status === 401) {
            window.location.href = "/?signin=1&next=" + encodeURIComponent(window.location.pathname);
            return;
          }
          if (error.fields && error.fields.content) {
            if (errorLabel) { errorLabel.hidden = false; errorLabel.textContent = error.fields.content; }
          } else {
            Folio.toast("error", "Couldn't post that note", error.message);
          }
        })
        .finally(function () {
          submitButton.disabled = false;
          submitButton.textContent = "Submit for review";
        });
    });
  }

  /* upvotes */
  document.querySelectorAll("[data-upvote]").forEach(function (button) {
    button.addEventListener("click", function () {
      if (button.disabled) return;
      button.disabled = true;
      Folio.api("/api/comments/" + button.getAttribute("data-upvote") + "/upvote", { method: "POST" })
        .then(function (data) {
          var label = button.querySelector("span");
          if (label) label.textContent = data.upvotes;
          Folio.toast("success", "Noted", "Your upvote was counted.");
        })
        .catch(function (error) {
          button.disabled = false;
          Folio.toast("error", "Couldn't upvote", error.message);
        });
    });
  });

  /* margin note navigation */
  document.querySelectorAll("[data-margin-target]").forEach(function (button) {
    button.addEventListener("click", function () {
      var block = document.getElementById("block-" + button.getAttribute("data-margin-target"));
      if (!block) return;
      block.scrollIntoView({ behavior: "smooth", block: "center" });
      block.classList.add("reader-block-target");
      window.setTimeout(function () { block.classList.remove("reader-block-target"); }, 1800);
      var comment = document.getElementById("comment-" + button.getAttribute("data-comment-target"));
      if (comment) {
        comment.scrollIntoView({ behavior: "smooth", block: "center" });
        comment.style.transition = "background-color .4s";
        comment.style.backgroundColor = "rgba(181,138,50,.18)";
        window.setTimeout(function () { comment.style.backgroundColor = ""; }, 1800);
      }
    });
  });

  document.querySelectorAll("[data-scroll-notes]").forEach(function (button) {
    button.addEventListener("click", function () {
      var notes = document.getElementById("notes");
      if (notes) notes.scrollIntoView({ behavior: "smooth" });
    });
  });

  /* ---------------------------------------------------------- reading progress */
  if (signedIn) {
    var lastSent = 0;
    function reportProgress() {
      var scrollable = document.documentElement.scrollHeight - window.innerHeight;
      var progress = scrollable > 0 ? Math.min(99, Math.round((window.scrollY / scrollable) * 100)) : 0;
      if (progress <= lastSent) return;
      lastSent = progress;
      Folio.api("/api/reading-progress", { json: { chapterId: chapterId, progress: progress } }).catch(function () { /* non-critical */ });
    }
    reportProgress();
    window.setInterval(reportProgress, 12000);
  }
})();
