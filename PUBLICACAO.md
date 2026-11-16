# Como publicar uma nova versão no NuGet

A publicação no nuget.org é **automática pelo GitHub Actions** e acontece **somente quando uma tag `v*` é enviada** (ex.: `v3.0.1`).
Merge na `main` **não publica** — apenas roda build e testes.

Não existe API key do NuGet para gerenciar: o CI usa **Trusted Publishing** (o GitHub gera uma chave temporária de 1 hora a cada publicação).

---

## Passo a passo

### 1. Alterar a versão

No arquivo `Marraia.Postgres/Marraia.Postgres.csproj`, atualize:

```xml
<AssemblyVersion>3.0.1.0</AssemblyVersion>
<FileVersion>3.0.1.0</FileVersion>
<Version>3.0.1</Version>
```

Atualize também o `<PackageReleaseNotes>` com o que mudou.

> **Qual número usar?** (versionamento semântico)
> - `3.0.0` → `3.0.1`: correção de bug, sem mudar a forma de usar.
> - `3.0.0` → `3.1.0`: funcionalidade nova, sem quebrar quem já usa.
> - `3.0.0` → `4.0.0`: mudança que quebra código de quem usa (breaking change).
> - Versão de teste: `3.1.0-alpha`, `3.1.0-beta` (aparece como pré-lançamento no NuGet).

### 2. Fazer o merge na `main`

Abra o PR, aguarde o CI ficar verde (build + testes) e faça o merge.

### 3. Criar e enviar a tag (é isso que publica)

```bash
git checkout main
git pull
git tag v3.0.1
git push origin v3.0.1
```

A tag **deve ser igual** ao `<Version>` do `.csproj`, com um `v` na frente.

### 4. Acompanhar

- **GitHub → aba Actions**: o workflow da tag deve terminar com todos os passos verdes, incluindo **NuGet login (OIDC)** e **Push to nuget.org**.
- **nuget.org**: a versão aparece em https://www.nuget.org/packages/Marraia.Postgres após **5 a 15 minutos** (o NuGet valida e indexa o pacote antes de listar).

---

## Configuração que já está feita (não precisa repetir)

### No GitHub — `.github/workflows/dotnet.yml`

```yaml
permissions:
  contents: read
  id-token: write          # permite ao GitHub gerar o token de identidade (OIDC)

# ...
- name: NuGet login (OIDC)
  if: startsWith(github.ref, 'refs/tags/v')
  id: nuget-login
  uses: NuGet/login@v1
  with:
    user: marraia          # nome do perfil no nuget.org

- name: Push to nuget.org
  if: startsWith(github.ref, 'refs/tags/v')
  run: dotnet nuget push ./artifacts/*.nupkg -k ${{ steps.nuget-login.outputs.NUGET_API_KEY }} -s https://api.nuget.org/v3/index.json --skip-duplicate
```

### No nuget.org — política de Trusted Publishing

nuget.org → clique no seu usuário → **Trusted Publishing**:

| Campo | Valor |
|---|---|
| Repository Owner | `marraia` |
| Repository | `marraia.postgress` |
| Workflow File | `dotnet.yml` (só o nome do arquivo) |
| Environment | (em branco) |
| Glob pattern / Packages | `Marraia.Postgres` (ou `Marraia.*`) |
| Scope | Push new packages and package versions |

> Se o nome do repositório ou do arquivo `dotnet.yml` mudar, a política precisa ser atualizada no nuget.org.

---

## Problemas comuns

| Sintoma | Causa | Solução |
|---|---|---|
| Fiz merge e não publicou | Merge não publica | Crie e envie a tag `vX.Y.Z` (passo 3). |
| Passo **Push to nuget.org** aparece como *skipped* | O workflow não rodou a partir de uma tag | Envie a tag. |
| Erro **403** no passo **NuGet login** | Falta a permissão `id-token: write` no workflow, ou a política no nuget.org não confere (repositório/arquivo diferentes) | Conferir o workflow e a política. |
| Erro **403** no passo **Push** | Política sem o pacote no *glob pattern*, ou versão já existe com outro dono | Conferir o campo de pacotes da política. |
| Erro *"At least one valid glob pattern or package must be specified"* ao criar a política | Faltou informar os pacotes | Preencher `Marraia.Postgres` no *glob pattern*. |
| A publicação falhou e preciso tentar de novo **com a mesma versão** | A tag já existe | Se o pacote **não** chegou ao NuGet, recrie a tag no commit corrigido:<br>`git push origin :refs/tags/v3.0.1`<br>`git tag -d v3.0.1`<br>`git tag v3.0.1`<br>`git push origin v3.0.1` |
| Publiquei uma versão com problema | O NuGet não permite apagar versões | Publique uma versão nova corrigida e, se quiser, oculte a antiga em nuget.org → pacote → **Manage → Listing** (*unlist*). |

---

## Outros pacotes Marraia (ex.: Marraia.MySql)

O mesmo processo funciona para os outros repositórios:

1. Copiar os passos **NuGet login (OIDC)** e **Push to nuget.org** e a permissão `id-token: write` para o workflow do repositório.
2. Criar uma política de Trusted Publishing no nuget.org com o nome daquele repositório e do arquivo de workflow dele (ou usar `Marraia.*` no *glob pattern*).
