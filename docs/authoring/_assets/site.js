/* pyNavis authoring docs - shared behaviour.
   Works from file:// - no fetch, no modules, no dependencies. The search index is a
   plain script (search-index.js) that assigns window.PYNAVIS_SEARCH, because XHR and
   fetch are blocked on file:// but <script src> is not. */
(function () {
  'use strict';

  /* ------------------------------------------------------------- theme -- */

  var STORE = 'pynavis-docs-theme';

  function applyTheme(t) {
    document.documentElement.setAttribute('data-theme', t);
    var btn = document.getElementById('theme-toggle');
    if (btn) btn.querySelector('span').textContent = t === 'dark' ? 'Light' : 'Dark';
  }

  function initTheme() {
    var saved = null;
    try { saved = localStorage.getItem(STORE); } catch (e) { /* file:// can block storage */ }
    var t = saved || (window.matchMedia &&
      window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    applyTheme(t);
    var btn = document.getElementById('theme-toggle');
    if (!btn) return;
    btn.addEventListener('click', function () {
      var next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      applyTheme(next);
      try { localStorage.setItem(STORE, next); } catch (e) {}
    });
  }

  /* -------------------------------------------------------------- copy -- */

  function initCopy() {
    var blocks = document.querySelectorAll('.code');
    for (var i = 0; i < blocks.length; i++) {
      (function (block) {
        var btn = block.querySelector('.copy-btn');
        if (!btn) return;
        btn.addEventListener('click', function () {
          var pre = block.querySelector('pre');
          if (!pre) return;
          var text = pre.innerText;
          var done = function () {
            btn.textContent = 'Copied';
            btn.className = 'copy-btn done';
            setTimeout(function () { btn.textContent = 'Copy'; btn.className = 'copy-btn'; }, 1400);
          };
          if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, function () { legacy(text, done); });
          } else {
            legacy(text, done);
          }
        });
      })(blocks[i]);
    }
  }

  function legacy(text, done) {
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); done(); } catch (e) {}
    document.body.removeChild(ta);
  }

  /* --------------------------------------------------------------- toc -- */

  function initToc() {
    var toc = document.querySelector('.toc');
    if (!toc) return;
    var links = toc.querySelectorAll('a');
    if (!links.length) return;

    var targets = [];
    for (var i = 0; i < links.length; i++) {
      var el = document.getElementById(links[i].getAttribute('href').slice(1));
      if (el) targets.push({ link: links[i], el: el });
    }

    var ticking = false;
    function spy() {
      ticking = false;
      var best = null;
      for (var i = 0; i < targets.length; i++) {
        if (targets[i].el.getBoundingClientRect().top <= 90) best = targets[i];
      }
      if (!best && targets.length) best = targets[0];
      for (var j = 0; j < targets.length; j++) {
        targets[j].link.className = targets[j].link.className.replace(/\s*active/, '');
      }
      if (best) best.link.className += ' active';
    }
    window.addEventListener('scroll', function () {
      if (!ticking) { ticking = true; window.requestAnimationFrame(spy); }
    }, { passive: true });
    spy();
  }

  /* ------------------------------------------------------------ search -- */

  function score(entry, needle) {
    var t = entry.t.toLowerCase();
    var p = entry.p.toLowerCase();
    var b = (entry.b || '').toLowerCase();
    if (t === needle) return 1000;
    if (t.indexOf(needle) === 0) return 700 - t.length;
    if (t.indexOf(needle) > -1) return 500 - t.length;
    if (p.indexOf(needle) > -1) return 220;
    if (b.indexOf(needle) > -1) return 140;
    // subsequence match on the title, so "clashgrp" still finds "clashgroup"
    var i = 0;
    for (var c = 0; c < t.length && i < needle.length; c++) {
      if (t.charAt(c) === needle.charAt(i)) i++;
    }
    return i === needle.length ? 60 : -1;
  }

  function highlight(text, needle) {
    var i = text.toLowerCase().indexOf(needle);
    if (i < 0) return esc(text);
    return esc(text.slice(0, i)) + '<mark>' + esc(text.slice(i, i + needle.length)) +
           '</mark>' + esc(text.slice(i + needle.length));
  }

  function esc(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  function initSearch() {
    var input = document.getElementById('search');
    var out = document.getElementById('search-results');
    if (!input || !out) return;
    var index = window.PYNAVIS_SEARCH || [];
    var base = document.body.getAttribute('data-base') || '';
    var sel = -1;
    var current = [];

    function close() { out.className = 'search-results'; sel = -1; current = []; }

    function render(q) {
      var needle = q.trim().toLowerCase();
      if (needle.length < 2) { close(); return; }

      var hits = [];
      for (var i = 0; i < index.length; i++) {
        var s = score(index[i], needle);
        if (s > 0) hits.push({ e: index[i], s: s });
      }
      hits.sort(function (a, b) { return b.s - a.s; });
      hits = hits.slice(0, 12);
      current = hits;

      if (!hits.length) {
        out.innerHTML = '<div class="sr-empty">No matches for &ldquo;' + esc(q) + '&rdquo;</div>';
        out.className = 'search-results open';
        return;
      }

      var html = '';
      for (var k = 0; k < hits.length; k++) {
        var e = hits[k].e;
        var body = e.b || '';
        if (body.length > 108) body = body.slice(0, 108) + '…';
        html += '<a href="' + base + esc(e.u) + '">' +
                '<span class="sr-title">' + highlight(e.t, needle) +
                ' <span class="sr-page">' + esc(e.p) + '</span></span>' +
                (body ? '<span class="sr-text">' + esc(body) + '</span>' : '') +
                '</a>';
      }
      out.innerHTML = html;
      out.className = 'search-results open';
      sel = -1;
    }

    function move(delta) {
      var links = out.querySelectorAll('a');
      if (!links.length) return;
      if (sel > -1 && links[sel]) links[sel].className = '';
      sel += delta;
      if (sel < 0) sel = links.length - 1;
      if (sel >= links.length) sel = 0;
      links[sel].className = 'sel';
      links[sel].scrollIntoView({ block: 'nearest' });
    }

    input.addEventListener('input', function () { render(input.value); });

    input.addEventListener('keydown', function (ev) {
      if (ev.key === 'ArrowDown') { ev.preventDefault(); move(1); }
      else if (ev.key === 'ArrowUp') { ev.preventDefault(); move(-1); }
      else if (ev.key === 'Enter') {
        var links = out.querySelectorAll('a');
        var target = sel > -1 ? links[sel] : links[0];
        if (target) { ev.preventDefault(); window.location.href = target.getAttribute('href'); }
      } else if (ev.key === 'Escape') { input.value = ''; close(); input.blur(); }
    });

    document.addEventListener('click', function (ev) {
      if (!out.contains(ev.target) && ev.target !== input) close();
    });

    document.addEventListener('keydown', function (ev) {
      var tag = (ev.target.tagName || '').toLowerCase();
      if (ev.key === '/' && tag !== 'input' && tag !== 'textarea') {
        ev.preventDefault();
        input.focus();
        input.select();
      }
    });
  }

  /* -------------------------------------------------------------- boot -- */

  function boot() {
    initTheme();
    initCopy();
    initToc();
    initSearch();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
