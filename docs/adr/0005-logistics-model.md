# 0005. Island storage pool fed by real transport jobs

Status: Accepted · Date: 2026-10-18

## Context

Fully simulated carts with per-agent pathfinding scale poorly to 5,000 buildings, while purely abstract logistics makes roads and distance meaningless. Anno's model sits between the two: goods are pooled island-wide, and distance limits throughput.

## Decision

- **One island-wide storage pool** of `Amount` per good. Goods enter it when a cart unloads at any storehouse.
- **Participation needs a road connection.** A building takes part only if it is road-connected to a storehouse whose reach covers it. A house also needs a Market's coverage before it can draw goods.
- **Producers** have an output buffer (default capacity 4 units). When it holds one cart load (default 2 units), the producer requests a **pickup job**.
- **Consumers** (producers with inputs) have an input buffer. When it drops below one cart load, they request a **delivery job** if the pool has the good.
- **Distance fields:** each storehouse holds a road-distance field over road tiles within its reach. The field is computed by Dijkstra with integer per-tile costs (dirt 1000, cobbled 800, paved 650, rail 300) and rebuilt only when roads within reach change.
- **Jobs:** each storehouse owns N carts. Pending requests are served in (request tick, building ID) order by the nearest storehouse with a free cart.
  - A job records its endpoints, its goods, its start tick and its phase durations.
  - Travel ticks = distance × base cart speed × (1000 − cartSpeed bonus) / 1000.
- **Visual replay only:** the view replays each job as a cart and pathfinds the route for display. The sim never pathfinds agents.
- **Construction** draws materials straight from the pool once the site is connected. Builders are abstract.
- **Houses** consume straight from the pool. Walkers are presentation-only.

## Consequences

- Sim cost scales with active jobs, roughly 2,000 at the scale target, rather than with agents.
- Road layout and the placement of storehouses and markets matter, without traffic simulation.
- There is no congestion. If it's ever wanted, it becomes a modifier on travel time, not agent simulation.
- Explain queries can trace a shortage through the chain: no producer, a stalled producer, no cart, or a storehouse out of reach.
