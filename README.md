# Aldehyde

An interactive map of perfumes. Every fragrance is a point, placed by what is in the bottle: perfumes sit close together when their notes and accords overlap.

Explore the map by scent family, search for a perfume, a note or a house, and see why two perfumes are close. Each neighbour is shown with the notes it shares. Dupes link a clone to the original it was inspired by, either named by the house itself or confirmed by the community.

Notes are as published by the house. Distance comes from shared notes and accords, not from reviews.

## Built with

- React and TypeScript (Vite), with deck.gl for the map
- ASP.NET Core minimal API on .NET 10, with EF Core
- UMAP to lay out the map from text embeddings of each perfume's notes
