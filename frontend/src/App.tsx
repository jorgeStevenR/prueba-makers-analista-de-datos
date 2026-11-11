import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { RutaProtegida, rutaInicio } from './auth/Guards'
import AdminPage from './pages/AdminPage'
import LoginPage from './pages/LoginPage'
import UsuarioPage from './pages/UsuarioPage'

function Inicio() {
  const { sesion } = useAuth()
  return <Navigate to={sesion ? rutaInicio(sesion.rol) : '/login'} replace />
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/prestamos"
            element={
              <RutaProtegida rol="Usuario">
                <UsuarioPage />
              </RutaProtegida>
            }
          />
          <Route
            path="/admin"
            element={
              <RutaProtegida rol="Admin">
                <AdminPage />
              </RutaProtegida>
            }
          />
          <Route path="*" element={<Inicio />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
