# Sprite Prefab Generator

Tạo prefab 2D hàng loạt từ một asset pack (Tiny Swords, pack itch.io, Asset Store...) trong Unity Editor.

Từ một thư mục nguồn, package tạo ba loại prefab:

| Nguồn | Prefab tạo ra |
| --- | --- |
| **Animator Controller có sẵn** (`.controller`) | SpriteRenderer + Animator dùng controller đó. Sprite ban đầu là frame đầu của state mặc định. |
| **Sprite sheet đã cắt** (texture nhiều sprite, chưa có clip nào dùng) | Tự tạo `.anim` cho từng sheet, gộp vào một `.controller`, rồi tạo prefab. |
| **Sprite đơn** (texture một sprite, không thuộc animation nào) | Prefab tĩnh, chỉ có SpriteRenderer. |

Texture đã được một clip có sẵn sử dụng sẽ không bị tạo animation hay prefab trùng lặp.

## Cài đặt

Qua git URL trong Package Manager (**+ > Install package from git URL**), hoặc thêm vào `Packages/manifest.json`:

```json
"com.haitndev.sprite-prefab-generator": "https://github.com/trinhhai1208/unity-sprite-prefab-generator.git#v0.1.0"
```

Phần `#v0.1.0` ghim phiên bản theo tag. Bỏ nó đi để luôn lấy bản mới nhất trên nhánh `main`.

Repo này là private: máy cài package phải có quyền truy cập repo, và git phải đăng nhập được GitHub (vd qua Git Credential Manager), vì Unity dùng git của hệ thống để tải package.

## Sử dụng

**Cách nhanh:** chuột phải một thư mục trong Project window > **Sprite Prefab Generator > Quick Generate (Default Settings)**. Danh sách prefab sẽ tạo được ghi ra Console và hỏi xác nhận trước khi tạo vào `Assets/Prefabs/<Tên thư mục>`.

**Dùng profile (khuyên dùng):**

1. Chuột phải thư mục nguồn > **Sprite Prefab Generator > Create Settings For Folder**. Profile được tạo trong `Assets/Settings/SpritePrefabGenerator/`.
2. Chỉnh cấu hình trong Inspector.
3. Bấm **Preview** để xem danh sách (không ghi gì), rồi **Generate Missing** hoặc **Rebuild All**.

**Tools > Sprite Prefab Generator > Generate Missing (All Profiles)** chạy mọi profile trong project, hữu ích sau khi import thêm asset.

- **Generate Missing** chỉ tạo prefab chưa có, không đụng tới prefab bạn đã chỉnh.
- **Rebuild All** ghi đè prefab (và animation tự tạo) nhưng giữ nguyên GUID, nên tham chiếu trong scene không bị hỏng. Chỉnh sửa thủ công trên prefab sẽ mất.

## Cấu hình

| Trường | Ý nghĩa |
| --- | --- |
| `sourceFolder` / `outputFolder` | Thư mục nguồn (quét cả thư mục con) và thư mục đầu ra. |
| `mirrorFolderStructure` | Giữ cấu trúc thư mục con của nguồn trong đầu ra. |
| `collapseFolderRegex` | Thư mục có tên khớp bị bỏ khỏi đường dẫn đầu ra. Mặc định bỏ các thư mục kiểu `"... Animations"`. |
| `excludePathRegex` | Bỏ qua asset có đường dẫn (tương đối với nguồn) khớp, vd `(?i)(tileset\|ui)`. |
| `fromAnimatorControllers` / `fromSpriteSheets` / `fromStaticSprites` | Bật/tắt từng loại nguồn. |
| `grouping` | Cách gộp sprite sheet thành controller: `Folder` (cả thư mục), `NamePrefix` (`Knight_Idle` + `Knight_Run` -> `Knight`), `PerTexture` (mỗi sheet một prefab). |
| `frameRate`, `minFrames` | FPS của clip tự tạo; số sprite tối thiểu để coi là animation. |
| `nonLoopingRegex` | Clip có tên khớp sẽ không lặp. Mặc định `death/die/dead`. |
| `defaultStateRegex` | Chọn state mặc định. Mặc định `idle`. |
| `animatedNamePattern` / `staticNamePattern` | Tên prefab. Token: `{name}`, `{parent}` (thư mục chứa asset), `{parent:N}` (từ thứ N, đếm từ 0, trong tên thư mục chứa). Vd `{name}_{parent:0}` với `Blue Buildings/Castle.png` -> `Castle_Blue`. |
| `template` | Prefab mẫu (vd đã có Collider2D, script). Được nhân bản rồi gán sprite/Animator vào object gốc. |
| `overrideSorting`, `sortingLayerName`, `orderInLayer` | Ghi đè sorting của SpriteRenderer. |

Animation tự tạo được lưu trong thư mục `Animations/` cạnh prefab. Tên clip có tiền tố là tên nhóm nếu tên texture chưa có, vd `Monk/Idle.png` -> `Monk_Idle`.

## Giới hạn

- Sprite sheet phải được cắt sẵn (Sprite Mode = Multiple, đã Slice trong Sprite Editor). Frame được sắp theo tên sprite (`Run_2` trước `Run_10`).
- Chưa hỗ trợ animation tách thành nhiều file ảnh riêng lẻ (`run_01.png`, `run_02.png`...).
- Chỉ hỗ trợ clip đổi sprite trên object gốc (`SpriteRenderer.m_Sprite`, path rỗng). Controller animate object con sẽ bị bỏ qua kèm cảnh báo.
- Controller tự tạo chỉ có các state, chưa có parameter/transition. Đổi animation bằng `animator.Play("Knight_Run")` hoặc tự thêm transition.

## Yêu cầu

Unity 2022.3 trở lên. Chỉ chạy trong Editor, không thêm gì vào bản build.
