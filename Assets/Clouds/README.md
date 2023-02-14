## A couple of improvements I could explore

### Mesh Gen
- Split noise samples by grids of lower resolutions, reconstruct the native resolution afterward by blending those sub resolutions like an FBM
- Make the mesh tillable by blending edge planes with their opposites

### Shader
- Fix sliver lining, do it after loop ? Once density has been figured out ?
- Proper clipping and rendering from a 'window' surface
- Use a compute shader
- Fix artifacts when inside clouds and reaching maximum fragment count, cannot detect inside from outside
- Pack ListNode' normals and depth into one float, see: Octahedron normal vector encoding
- Investigate approaches to reduce overdraw