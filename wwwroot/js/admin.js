/* Folio — editorial dashboard interactions */
(function () {
  "use strict";
  var Folio = window.Folio;

  /* ------------------------------------------------------------ tabs */
  var tabs = document.querySelectorAll("[data-tab]");
  tabs.forEach(function (tab) {
    tab.addEventListener("click", function () {
      tabs.forEach(function (other) {
        other.setAttribute("aria-selected", other === tab ? "true" : "false");
      });
      var target = tab.getAttribute("data-tab");
      ["moderation", "manuscripts"].forEach(function (name) {
        var panel = document.getElementById("panel-" + name);
        if (panel) panel.hidden = name !== target;
      });
    });
  });

  /* ------------------------------------------------------------ upload modal */
  var uploadModal = document.getElementById("upload-modal");
  var uploadForm = document.getElementById("upload-form");
  var coverInput = document.getElementById("cover-input");
  var coverDrop = document.getElementById("cover-drop");
  var coverHint = document.getElementById("cover-hint");
  var coverName = document.getElementById("cover-name");
  var removeCover = document.getElementById("remove-cover");
  var synopsisField = document.getElementById("book-synopsis");
  var synopsisCount = document.getElementById("synopsis-count");
  var uploadError = document.getElementById("upload-error");
  var uploadSubmit = document.getElementById("upload-submit");
  var selectedCover = null;

  function openUpload() {
    uploadForm.reset();
    selectedCover = null;
    coverDrop.classList.remove("has-error");
    coverDrop.innerHTML = '<span class="cover-drop-hint" id="cover-hint">JPG PNG WebP<br>≤ 4 MB</span>';
    coverName.textContent = "";
    removeCover.hidden = true;
    synopsisCount.textContent = "0/4000";
    uploadError.hidden = true;
    clearErrors(uploadForm);
    Folio.openModal(uploadModal);
  }

  ["open-upload", "open-upload-empty"].forEach(function (id) {
    var trigger = document.getElementById(id);
    if (trigger) trigger.addEventListener("click", openUpload);
  });

  function clearErrors(scope) {
    scope.querySelectorAll("[data-error-for]").forEach(function (label) {
      label.hidden = true;
      label.textContent = "";
    });
    scope.querySelectorAll(".field").forEach(function (field) { field.classList.remove("input-error"); });
  }

  function showErrors(scope, fields) {
    Object.keys(fields || {}).forEach(function (key) {
      var label = scope.querySelector('[data-error-for="' + key + '"]');
      if (label) {
        label.textContent = fields[key];
        label.hidden = false;
        var input = scope.querySelector("#" + (key === "cover" ? "cover-drop" : "book-" + key.toLowerCase()));
        if (input) input.classList.add("input-error");
      }
    });
  }

  ["choose-cover", "cover-drop"].forEach(function (id) {
    var trigger = document.getElementById(id);
    if (trigger) trigger.addEventListener("click", function () { coverInput.click(); });
  });

  if (coverInput) {
    coverInput.addEventListener("change", function () {
      var file = coverInput.files && coverInput.files[0];
      if (!file) return;
      if (["image/jpeg", "image/png", "image/webp"].indexOf(file.type) === -1) {
        uploadError.hidden = false;
        uploadError.textContent = "Only JPG, PNG or WebP images are allowed.";
        coverInput.value = "";
        return;
      }
      if (file.size > 4 * 1024 * 1024) {
        uploadError.hidden = false;
        uploadError.textContent = "That image is too large. Please upload one under 4 MB.";
        coverInput.value = "";
        return;
      }
      uploadError.hidden = true;
      selectedCover = file;
      var reader = new FileReader();
      reader.onload = function () {
        coverDrop.innerHTML = '<img src="' + reader.result + '" alt="Cover preview">';
        coverDrop.classList.remove("has-error");
      };
      reader.readAsDataURL(file);
      coverName.textContent = file.name;
      removeCover.hidden = false;
    });
  }

  if (removeCover) {
    removeCover.addEventListener("click", function () {
      selectedCover = null;
      coverInput.value = "";
      coverDrop.innerHTML = '<span class="cover-drop-hint">JPG PNG WebP<br>≤ 4 MB</span>';
      coverName.textContent = "";
      removeCover.hidden = true;
    });
  }

  if (synopsisField && synopsisCount) {
    synopsisField.addEventListener("input", function () {
      synopsisCount.textContent = synopsisField.value.length + "/4000";
    });
  }

  if (uploadForm) {
    uploadForm.addEventListener("submit", function (event) {
      event.preventDefault();
      clearErrors(uploadForm);
      uploadError.hidden = true;

      var title = document.getElementById("book-title").value.trim();
      var category = document.getElementById("book-category").value;
      var authorName = document.getElementById("book-author").value.trim();
      var synopsis = synopsisField.value.trim();
      var issues = {};
      if (title.length < 2) issues.title = "Enter the book name.";
      if (!category) issues.category = "Choose a category.";
      if (authorName.length < 2) issues.authorName = "Enter the author's name.";
      if (synopsis.length < 40) issues.synopsis = "Write at least 40 characters of synopsis.";
      if (!selectedCover) issues.cover = "A cover image is required.";

      if (Object.keys(issues).length) {
        showErrors(uploadForm, issues);
        uploadError.hidden = false;
        uploadError.textContent = "Every field is required. Please check the highlighted ones.";
        return;
      }

      var body = new FormData();
      body.append("title", title);
      body.append("category", category);
      body.append("authorName", authorName);
      body.append("synopsis", synopsis);
      body.append("status", document.getElementById("book-status").value);
      body.append("cover", selectedCover);

      uploadSubmit.disabled = true;
      uploadSubmit.innerHTML = '<span class="spinner"></span> Publishing…';

      Folio.api("/api/admin/books", { body: body })
        .then(function (data) {
          Folio.closeModal(uploadModal);
          Folio.toast("success", "Book published", "“" + data.book.title + "” is now in the catalogue.");
          window.setTimeout(function () { window.location.reload(); }, 900);
        })
        .catch(function (error) {
          uploadError.hidden = false;
          uploadError.textContent = error.message;
          showErrors(uploadForm, error.fields);
        })
        .finally(function () {
          uploadSubmit.disabled = false;
          uploadSubmit.textContent = "Publish book";
        });
    });
  }

  /* ------------------------------------------------------------ chapter modal */
  var chapterModal = document.getElementById("chapter-modal");
  var chapterForm = document.getElementById("chapter-form");
  var chapterBookId = document.getElementById("chapter-book-id");
  var chapterNumber = document.getElementById("chapter-number");
  var chapterContent = document.getElementById("chapter-content");
  var chapterError = document.getElementById("chapter-error");
  var chapterSubmit = document.getElementById("chapter-submit");
  var paragraphCount = document.getElementById("paragraph-count");

  function openChapterFor(bookId, title, nextNumber) {
    chapterForm.reset();
    clearErrors(chapterForm);
    chapterError.hidden = true;
    chapterBookId.value = bookId;
    chapterNumber.value = nextNumber || "1";
    document.getElementById("chapter-title").textContent = "Add a chapter to " + title;
    document.getElementById("chapter-publish").checked = true;
    paragraphCount.textContent = "0 paragraphs";
    Folio.openModal(chapterModal);
  }

  document.querySelectorAll("[data-add-chapter]").forEach(function (button) {
    button.addEventListener("click", function () {
      var manuscript = button.closest("[data-manuscript]");
      openChapterFor(
        manuscript.getAttribute("data-book-id"),
        manuscript.getAttribute("data-title"),
        manuscript.getAttribute("data-next-chapter"));
    });
  });

  if (chapterContent) {
    chapterContent.addEventListener("input", function () {
      var count = chapterContent.value.split(/\n\s*\n/).filter(function (part) { return part.trim().length; }).length;
      paragraphCount.textContent = count + (count === 1 ? " paragraph" : " paragraphs");
    });
  }

  if (chapterForm) {
    chapterForm.addEventListener("submit", function (event) {
      event.preventDefault();
      clearErrors(chapterForm);
      chapterError.hidden = true;

      var body = new FormData();
      body.append("bookId", chapterBookId.value);
      body.append("title", document.getElementById("chapter-name").value);
      body.append("chapterNumber", chapterNumber.value);
      body.append("content", chapterContent.value);
      body.append("audioUrl", document.getElementById("chapter-audio").value);
      var chapterPublish = document.getElementById("chapter-publish").checked;
      var chapterScope = document.getElementById("chapter-scope");
      body.append("publish", chapterPublish ? "true" : "false");
      var fullBook = document.getElementById("chapter-full-book");
      if (fullBook && fullBook.checked) body.append("fullBook", "true");
      if (chapterScope && chapterScope.value) {
        body.append("scope", chapterScope.value);
      }
      body.append("publishScope", chapterPublish ? (chapterScope && chapterScope.value ? chapterScope.value : "chapter") : "none");

      chapterSubmit.disabled = true;
      chapterSubmit.innerHTML = '<span class="spinner"></span> Saving…';

      Folio.api("/api/admin/chapters", { body: body })
        .then(function (data) {
          Folio.closeModal(chapterModal);
          if (fullBook && fullBook.checked && data && data.chapters) {
            Folio.toast("success", "Full book uploaded", data.message || (data.chapters + " chapters added."));
          } else {
            Folio.toast("success", "Chapter saved", "The manuscript has a new chapter.");
          }
          window.setTimeout(function () { window.location.reload(); }, 800);
        })
        .catch(function (error) {
          chapterError.hidden = false;
          chapterError.textContent = error.message;
          if (error.fields) {
            Object.keys(error.fields).forEach(function (key) {
              var label = chapterForm.querySelector('[data-error-for="' + key + '"]');
              if (label) { label.textContent = error.fields[key]; label.hidden = false; }
            });
          }
        })
        .finally(function () {
          chapterSubmit.disabled = false;
          chapterSubmit.textContent = "Save chapter";
        });
    });
  }

  /* ------------------------------------------------------------ moderation */
  var contributorModal = document.getElementById("contributor-modal");
  var pendingCount = document.getElementById("pending-count");
  var contributorTarget = null;

  function updatePendingCount(delta) {
    if (!pendingCount) return;
    var value = Math.max(0, (parseInt(pendingCount.textContent, 10) || 0) + delta);
    pendingCount.textContent = value;
    var empty = document.getElementById("queue-empty");
    var list = document.getElementById("queue-list");
    if (list && list.children.length === 0 && !empty) {
      var panel = document.getElementById("panel-moderation").firstElementChild;
      var message = document.createElement("div");
      message.className = "empty-state";
      message.id = "queue-empty";
      message.innerHTML = '<p class="eyebrow">All clear</p><h3>The queue is clear</h3><p>New notes and contributions will appear here the moment a reader submits one.</p>';
      panel.appendChild(message);
    }
  }

  document.querySelectorAll("[data-moderate]").forEach(function (button) {
    button.addEventListener("click", function () {
      var commentId = button.getAttribute("data-comment-id");
      var action = button.getAttribute("data-moderate");
      var card = document.querySelector('[data-queue-id="' + commentId + '"]');
      var buttons = card ? card.querySelectorAll("button") : [];
      Array.prototype.forEach.call(buttons, function (item) { item.disabled = true; });

      Folio.api("/api/admin/moderation/" + commentId, { method: "POST", json: { action: action } })
        .then(function (data) {
          if (card) card.remove();
          updatePendingCount(-1);

          if (action === "accept") {
            Folio.toast("success", "Note published", "The note is now visible on the chapter.");
            var contribution = card && card.getAttribute("data-contribution") === "true";
            if (contribution) {
              contributorTarget = {
                bookId: card.getAttribute("data-book-id"),
                userId: card.getAttribute("data-user-id"),
                userName: card.getAttribute("data-user-name"),
                bookTitle: card.getAttribute("data-book-title")
              };
              document.getElementById("contributor-sub").textContent = "This contribution from " + contributorTarget.userName + " was accepted.";
              document.getElementById("contributor-question").textContent =
                "Would you like to officially add " + contributorTarget.userName + " as a contributor to this book?";
              document.getElementById("contributor-copy").textContent =
                "They'll be credited on “" + contributorTarget.bookTitle + "”, their role becomes author if they were a reader, and they earn 25 reputation plus the Contributor badge.";
              Folio.openModal(contributorModal);
            }
          } else {
            Folio.toast("info", "Note rejected", "It stays hidden from readers.");
          }
        })
        .catch(function (error) {
          Array.prototype.forEach.call(buttons, function (item) { item.disabled = false; });
          Folio.toast("error", "Couldn't update that note", error.message);
        });
    });
  });

  var contributorConfirm = document.getElementById("contributor-confirm");
  if (contributorConfirm) {
    contributorConfirm.addEventListener("click", function () {
      if (!contributorTarget) return;
      contributorConfirm.disabled = true;
      contributorConfirm.innerHTML = '<span class="spinner"></span> Crediting…';
      Folio.api("/api/admin/contributors", {
        json: { bookId: contributorTarget.bookId, userId: contributorTarget.userId }
      })
        .then(function (data) {
          Folio.closeModal(contributorModal);
          Folio.toast("success", "Contributor credited", data.username + " is now credited on “" + data.bookTitle + "”.");
          contributorTarget = null;
        })
        .catch(function (error) {
          Folio.toast("error", "Couldn't add the contributor", error.message);
        })
        .finally(function () {
          contributorConfirm.disabled = false;
          contributorConfirm.textContent = "Add to credits";
        });
    });
  }

  /* ------------------------------------------------------------ book information */
  var bookInfoModal = document.getElementById("book-info-modal");
  var infoTarget = null;

  function manuscriptRows() {
    return Array.prototype.slice.call(document.querySelectorAll("[data-manuscript]"));
  }

  function openBookInfo(row) {
    if (!row || !bookInfoModal) return;
    infoTarget = row;
    document.getElementById("info-book-id").value = row.getAttribute("data-book-id");
    document.getElementById("info-title").value = row.getAttribute("data-title") || "";
    document.getElementById("info-author").value = row.getAttribute("data-author") || "";
    document.getElementById("info-category").value = row.getAttribute("data-category") || "";
    document.getElementById("info-synopsis").value = row.getAttribute("data-synopsis") || "";
    document.getElementById("info-status").value = row.getAttribute("data-status") || "ongoing";
    var next = parseInt(row.getAttribute("data-next-chapter"), 10) || 1;
    document.getElementById("book-info-chapters").textContent =
      (next - 1) + (next - 1 === 1 ? " chapter" : " chapters") + " so far. The next upload becomes chapter " + next + ".";
    document.getElementById("info-open-book").href = "/books/" + (row.getAttribute("data-slug") || "");
    document.getElementById("book-info-error").hidden = true;
    clearErrors(bookInfoModal);
    Folio.openModal(bookInfoModal);
  }

  document.querySelectorAll("[data-book-edit]").forEach(function (button) {
    button.addEventListener("click", function () {
      openBookInfo(button.closest("[data-manuscript]"));
    });
  });

  var lookup = document.getElementById("book-lookup");
  var lookupResults = document.getElementById("book-lookup-results");
  if (lookup && lookupResults) {
    function renderLookup() {
      var term = lookup.value.trim().toLowerCase();
      var matches = manuscriptRows().filter(function (row) {
        return !term || (row.getAttribute("data-filter") || "").indexOf(term) !== -1;
      });
      lookupResults.innerHTML = "";
      if (!matches.length) {
        lookupResults.innerHTML = '<p class="book-lookup-empty">No book matches that name yet. Create it with “Upload book”, then add chapters any time.</p>';
      } else {
        matches.slice(0, 8).forEach(function (row) {
          var option = document.createElement("button");
          option.type = "button";
          option.className = "book-lookup-option";
          var next = parseInt(row.getAttribute("data-next-chapter"), 10) || 1;
          option.innerHTML = "<div><strong>" + Folio.escapeHtml(row.getAttribute("data-title")) + "</strong>" +
            "<span>" + Folio.escapeHtml(row.getAttribute("data-author")) + " · " + (next - 1) + " chapters</span></div>";
          option.addEventListener("click", function () {
            lookupResults.hidden = true;
            lookup.setAttribute("aria-expanded", "false");
            openBookInfo(row);
          });
          lookupResults.appendChild(option);
        });
      }
      lookupResults.hidden = false;
      lookup.setAttribute("aria-expanded", "true");
    }

    lookup.addEventListener("input", renderLookup);
    lookup.addEventListener("focus", function () { renderLookup(); });
    document.addEventListener("click", function (event) {
      if (lookupResults.hidden) return;
      if (event.target.closest(".book-lookup")) return;
      lookupResults.hidden = true;
      lookup.setAttribute("aria-expanded", "false");
    });
  }

  var infoAddChapter = document.getElementById("info-add-chapter");
  if (infoAddChapter) {
    infoAddChapter.addEventListener("click", function () {
      if (!infoTarget) return;
      var bookId = infoTarget.getAttribute("data-book-id");
      var title = document.getElementById("info-title").value.trim() || infoTarget.getAttribute("data-title");
      var next = infoTarget.getAttribute("data-next-chapter") || "1";
      Folio.closeModal(bookInfoModal);
      window.setTimeout(function () { openChapterFor(bookId, title, next); }, 90);
    });
  }

  var bookInfoSave = document.getElementById("book-info-save");
  if (bookInfoSave) {
    bookInfoSave.addEventListener("click", function () {
      if (!infoTarget) return;
      var errorLabel = document.getElementById("book-info-error");
      errorLabel.hidden = true;
      clearErrors(bookInfoModal);

      var payload = {
        title: document.getElementById("info-title").value.trim(),
        category: document.getElementById("info-category").value,
        authorName: document.getElementById("info-author").value.trim(),
        synopsis: document.getElementById("info-synopsis").value.trim(),
        status: document.getElementById("info-status").value
      };

      bookInfoSave.disabled = true;
      bookInfoSave.innerHTML = '<span class="spinner"></span> Saving…';
      Folio.api("/api/admin/books/" + infoTarget.getAttribute("data-book-id") + "/info", {
        method: "PATCH",
        json: payload
      })
        .then(function (data) {
          var book = (data && data.book) || payload;
          infoTarget.setAttribute("data-title", payload.title);
          infoTarget.setAttribute("data-author", payload.authorName);
          infoTarget.setAttribute("data-category", payload.category);
          infoTarget.setAttribute("data-synopsis", payload.synopsis);
          infoTarget.setAttribute("data-status", payload.status);
          infoTarget.setAttribute("data-filter", (payload.title + " " + payload.authorName + " " + payload.category).toLowerCase());
          var heading = infoTarget.querySelector(".manuscript-title h3");
          if (heading) heading.textContent = payload.title;
          var meta = infoTarget.querySelector(".manuscript-meta");
          if (meta) meta.textContent = "by " + payload.authorName + " · updated just now";
          var cover = infoTarget.querySelector(".manuscript-cover img");
          if (cover) cover.setAttribute("alt", payload.title);
          Folio.closeModal(bookInfoModal);
          Folio.toast("success", "Book updated", "“" + (book.title || payload.title) + "” was saved.");
        })
        .catch(function (error) {
          errorLabel.hidden = false;
          errorLabel.textContent = error.message;
          showErrors(bookInfoModal, error.fields);
          Folio.toast("error", "Couldn't save the book", error.message);
        })
        .finally(function () {
          bookInfoSave.disabled = false;
          bookInfoSave.textContent = "Save changes";
        });
    });
  }

  var fullBookToggle = document.getElementById("chapter-full-book");
  if (fullBookToggle) {
    fullBookToggle.addEventListener("change", function () {
      var headingFields = document.getElementById("chapter-heading-fields");
      if (headingFields) headingFields.style.opacity = fullBookToggle.checked ? "0.45" : "1";
    });
  }

  /* ------------------------------------------------------------ manuscripts */
  var filterInput = document.getElementById("manuscript-filter");
  if (filterInput) {
    filterInput.addEventListener("input", function () {
      var term = filterInput.value.trim().toLowerCase();
      document.querySelectorAll("[data-manuscript]").forEach(function (row) {
        var haystack = row.getAttribute("data-filter") || "";
        row.hidden = term.length > 0 && haystack.indexOf(term) === -1;
      });
    });
  }

  document.querySelectorAll("[data-status-select]").forEach(function (select) {
    select.addEventListener("change", function () {
      var manuscript = select.closest("[data-manuscript]");
      select.disabled = true;
      Folio.api("/api/admin/books/" + manuscript.getAttribute("data-book-id"), {
        method: "PATCH",
        json: { status: select.value }
      })
        .then(function () {
          Folio.toast("success", "Status updated", "“" + manuscript.getAttribute("data-title") + "” is now " + select.value + ".");
        })
        .catch(function (error) {
          Folio.toast("error", "Couldn't update the book", error.message);
        })
        .finally(function () { select.disabled = false; });
    });
  });

  var deleteModal = document.getElementById("delete-modal");
  var deleteTarget = null;
  document.querySelectorAll("[data-delete-book]").forEach(function (button) {
    button.addEventListener("click", function () {
      var manuscript = button.closest("[data-manuscript]");
      deleteTarget = { id: manuscript.getAttribute("data-book-id"), title: manuscript.getAttribute("data-title") };
      document.getElementById("delete-copy").textContent =
        "You're about to delete “" + deleteTarget.title + "” and every chapter, note and credit attached to it. This cannot be undone.";
      Folio.openModal(deleteModal);
    });
  });

  var deleteConfirm = document.getElementById("delete-confirm");
  if (deleteConfirm) {
    deleteConfirm.addEventListener("click", function () {
      if (!deleteTarget) return;
      deleteConfirm.disabled = true;
      deleteConfirm.innerHTML = '<span class="spinner"></span> Deleting…';
      Folio.api("/api/admin/books/" + deleteTarget.id, { method: "DELETE" })
        .then(function () {
          var row = document.querySelector('[data-manuscript][data-book-id="' + deleteTarget.id + '"]');
          if (row) row.remove();
          Folio.closeModal(deleteModal);
          Folio.toast("success", "Book removed", "The title and its chapters were deleted.");
          deleteTarget = null;
          window.setTimeout(function () { window.location.reload(); }, 700);
        })
        .catch(function (error) {
          Folio.toast("error", "Couldn't delete the book", error.message);
        })
        .finally(function () {
          deleteConfirm.disabled = false;
          deleteConfirm.textContent = "Delete permanently";
        });
    });
  }
})();
