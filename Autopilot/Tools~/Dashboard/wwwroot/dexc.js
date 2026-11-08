// The small client every page of the dashboard uses, the project's pages too. It holds the token of this run, reads
// and writes the API, listens to its events, and draws the bar of pages at the top.
//
//   <link rel="stylesheet" href="/style.css">
//   <script src="/dexc.js"></script>
//   Dexc.nav();
//   Dexc.on("build", build => ...);           // every look at a build: { name, running, error, updated, status }
//   Dexc.on("graph", ({ name, graph }) => ...); // the machines of a build, when its bot was loaded
//   const builds = await Dexc.get("/api/builds");
//   await Dexc.post("/api/...", { ... });     // changes need the token, which this adds
(function () {
	"use strict";
	const key = "dexc-dashboard-token";
	const fromUrl = new URLSearchParams(location.search).get("token");
	let token = null;
	try {
		if (fromUrl) sessionStorage.setItem(key, fromUrl);
		token = fromUrl || sessionStorage.getItem(key);
	} catch (error) {
		token = fromUrl;
	}

	let source = null;
	const handlers = {};

	function listen() {
		if (source) return;
		source = new EventSource("/api/events");
		for (const name of Object.keys(handlers)) attach(name);
	}

	function attach(name) {
		source.addEventListener(name, message => {
			const data = JSON.parse(message.data);
			for (const handler of handlers[name]) handler(data);
		});
	}

	async function request(method, path, body) {
		const headers = { "Content-Type": "application/json" };
		if (token) headers["X-Dexc-Token"] = token;
		const response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
		if (response.status === 204) return null;
		if (!response.ok) throw new Error(method + " " + path + ": " + response.status + " " + (await response.text()));
		const text = await response.text();
		return text ? JSON.parse(text) : null;
	}

	function element(tag, attributes, ...children) {
		const node = document.createElement(tag);
		for (const [name, value] of Object.entries(attributes || {})) {
			if (value === undefined || value === null || value === false) continue;
			if (name === "class") node.className = value;
			else if (name.startsWith("on")) node.addEventListener(name.substring(2), value);
			else node.setAttribute(name, value === true ? "" : value);
		}
		for (const child of children.flat()) {
			if (child === undefined || child === null || child === false) continue;
			node.append(child instanceof Node ? child : document.createTextNode(String(child)));
		}
		return node;
	}

	async function nav() {
		const bar = element("nav", { class: "dexc-nav" },
			element("a", { href: "/" }, "Builds"),
			element("a", { href: "/machines.html" }, "Machines"));
		document.body.prepend(bar);
		for (const link of bar.querySelectorAll("a")) {
			if (new URL(link.href).pathname === location.pathname) link.classList.add("current");
		}
		try {
			const pages = await request("GET", "/api/pages");
			if (pages.length) bar.append(element("span", { class: "dexc-nav-gap" }, "Project"));
			for (const page of pages) {
				const link = element("a", { href: "/project/" + encodeURIComponent(page) }, page.replace(/\.html$/i, ""));
				if (location.pathname === link.pathname) link.classList.add("current");
				bar.append(link);
			}
		} catch (error) {
			console.warn(error);
		}
	}

	window.Dexc = {
		get token() { return token; },
		get: path => request("GET", path),
		post: (path, body) => request("POST", path, body),
		on(name, handler) {
			if (!handlers[name]) {
				handlers[name] = [];
				if (source) attach(name);
			}
			handlers[name].push(handler);
			listen();
		},
		element,
		nav,
	};
})();
