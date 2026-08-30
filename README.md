<h3 align="center">
    <a href="#"><img style="height:300px;" align="center" src="SolasPoster.png"></a>
</h3>
<div align="center">
    
[![Site](https://img.shields.io/badge/Engine%20site-orange)](https://solas-org.github.io/solas-website/)
[![NuGet Version](https://img.shields.io/nuget/v/Solas)](https://nuget.org/packages/Solas)
[![Join our Telegram](https://img.shields.io/badge/Join_our-Telegram-blue?logo=telegram)](https://t.me/SolasEngine)
[![Sponsor us](https://img.shields.io/badge/Sponsor%20us-pink)](https://pay.cloudtips.ru/p/65fe2c6c)
    
</div>

<div>

<h1 align="center"><strong>⭐ Solas Game Engine ⭐</strong></h1>

<h3 align="center"><em>“Developers should build games, not fight the engine.”</em></h3>

The ultimate goal of this engine is to eliminate architectural friction. We believe that when an engine prioritizes the developer, the developer can truly focus on the player.

> 💡 **The Core Philosophy:**
> - **DX (Developer Experience)** is the engine architect's top priority.
> - **UX (User Experience)** is the game developer's top priority.
> 
> Only this synergy allows teams to build outstanding games at lightning speed.

---

<h2 strong align="center"><strong>Overview 🚀</strong></h2>

Designed for rapid development across projects of any scale—with a special soft spot for **indie developers** — this engine redefines modern game workflows. 

By prioritizing exceptional **DX**, it solves the most painful bottlenecks of traditional engines. Built from the ground up with **teamwork, Git-friendliness, and CI/CD automation** in mind, its architecture naturally separates concerns, virtually eliminating merge conflicts and version control nightmares.

<h3 strong align="center"><strong>Powered by .NET 10 ⚡</strong></h3>

The engine leverages the cutting-edge capabilities of .NET 10, delivering out-of-the-box support for:
- **True Multithreading & Parallel Computing** – Max out modern hardware without the boilerplate.
- **Data-Oriented Design (DOD)** – Cache-friendly, high-performance architecture.
- **Reactive State Management** – Real-time, reactive game data updates during gameplay.
- **Flexible Data Modifiers** – Easily tweak, scale, and transform data on the fly.
- **Spatial Partitioning** – Advanced world and space management.
- **Robust Dependency Injection (DI)** – Clean, decoupled, and highly testable code.
- *...and much more.*

---

<h2 strong align="center"><strong>Architecture: The EDL Pattern 🏗️</strong></h2>

The engine introduces **EDL**, a breakthrough architectural pattern that fuses the best aspects of traditional OOP (Object-Oriented Programming), COP (Component-Oriented Programming), and ECS (Entity Component System).
### The Three Pillars of EDL:
* **`Entity`** – A lightweight, empty container holding unique metadata.
* **`Data`** – Pure data structures or classes: you chose. No logic, just state.
* **`Logic`** – Ultra-lightweight logic classes that hold only a reference to an `Entity` and operate on its `Data`.

This separation guarantees that your codebase remains modular, highly performant, and incredibly easy to scale as your project grows.

</div>
